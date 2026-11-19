// Copyright (c) ArchScada. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Scada.Server.Modules.ModRuleEngine.Logic
{
    /// <summary>
    /// Evaluates SCADA channel data against configured rules using compiled LINQ expressions.
    /// <para>Вычисляет данные каналов SCADA по настроенным правилам с помощью скомпилированных выражений LINQ.</para>
    /// </summary>
    public class RuleEngineService : IDisposable
    {
        private readonly List<RuleConfig> rules;
        private readonly Dictionary<string, Delegate> compiledRules = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastFireTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public RuleEngineService(List<RuleConfig> rules)
        {
            this.rules = rules ?? new List<RuleConfig>();
        }

        /// <summary>
        /// Compiles all enabled rules into LINQ expression delegates for fast evaluation.
        /// <para>Компилирует все включённые правила в делегаты выражений LINQ для быстрого вычисления.</para>
        /// </summary>
        public void Compile()
        {
            compiledRules.Clear();
            foreach (var rule in rules.Where(r => r.Enabled))
            {
                try
                {
                    var del = CompileRule(rule);
                    compiledRules[rule.RuleName] = del;
                }
                catch
                {
                    // Compilation errors are surfaced by Evaluate returning failed results.
                }
            }
        }

        /// <summary>
        /// Evaluates all rules against the provided input and returns the results.
        /// <para>Вычисляет все правила относительно предоставленного входного объекта и возвращает результаты.</para>
        /// </summary>
        public List<RuleResult> Evaluate(RuleInput input)
        {
            var results = new List<RuleResult>();
            var now = DateTime.UtcNow;

            foreach (var rule in rules.Where(r => r.Enabled))
            {
                if (rule.MinIntervalMs > 0)
                {
                    if (lastFireTime.TryGetValue(rule.RuleName, out var last))
                    {
                        if ((now - last).TotalMilliseconds < rule.MinIntervalMs)
                            continue;
                    }
                    lastFireTime[rule.RuleName] = now;
                }

                if (!compiledRules.TryGetValue(rule.RuleName, out var del))
                    continue;

                try
                {
                    var isSuccess = (bool)del.DynamicInvoke(input);
                    if (isSuccess)
                    {
                        lastFireTime[rule.RuleName] = now;
                        results.Add(new RuleResult
                        {
                            RuleName = rule.RuleName,
                            IsSuccess = true,
                            SuccessEvent = rule.SuccessEvent,
                            ErrorMessage = rule.ErrorMessage,
                            Actions = rule.Actions
                        });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new RuleResult
                    {
                        RuleName = rule.RuleName,
                        IsSuccess = false,
                        SuccessEvent = rule.SuccessEvent,
                        ErrorMessage = "Rule evaluation error: " + ex.Message,
                        Actions = new List<RuleAction>()
                    });
                }
            }
            return results;
        }

        private Delegate CompileRule(RuleConfig rule)
        {
            var inputParam = Expression.Parameter(typeof(RuleInput), "input");
            var body = ParseExpression(rule.Expression, inputParam);
            var lambda = Expression.Lambda<Func<RuleInput, bool>>(body, inputParam);
            return lambda.Compile();
        }

        private Expression ParseExpression(string expr, ParameterExpression inputParam)
        {
            expr = expr.Trim();
            if (expr.StartsWith("Channels[", StringComparison.OrdinalIgnoreCase))
            {
                var aliasStart = expr.IndexOf('"');
                var aliasEnd = expr.LastIndexOf('"');
                if (aliasStart < 0 || aliasEnd <= aliasStart)
                    throw new InvalidOperationException("Invalid Channels[...] syntax in rule expression.");
                var alias = expr.Substring(aliasStart + 1, aliasEnd - aliasStart - 1);
                var channelsProp = Expression.Property(inputParam, nameof(RuleInput.Channels));
                var indexer = typeof(Dictionary<string, double>).GetProperty("Item")!;
                return Expression.Call(channelsProp, indexer.GetGetMethod(), Expression.Constant(alias));
            }

            if (double.TryParse(expr, out var num))
                return Expression.Constant(num);

            if (expr == "true") return Expression.Constant(true);
            if (expr == "false") return Expression.Constant(false);

            var op = FindTopLevelOperator(expr, new[] { "&&", "||" });
            if (op != null)
            {
                var left = expr.Substring(0, op.Value.Index).Trim();
                var right = expr.Substring(op.Value.Index + op.Value.Op.Length).Trim();
                var leftExpr = ParseExpression(left, inputParam);
                var rightExpr = ParseExpression(right, inputParam);
                return op.Value.Op switch
                {
                    "&&" => Expression.AndAlso(leftExpr, rightExpr),
                    "||" => Expression.OrElse(leftExpr, rightExpr),
                    _ => throw new InvalidOperationException("Unknown logical operator: " + op.Value.Op)
                };
            }

            op = FindTopLevelOperator(expr, new[] { "==", "!=", "<=", ">=", "<", ">" });
            if (op != null)
            {
                var left = expr.Substring(0, op.Value.Index).Trim();
                var right = expr.Substring(op.Value.Index + op.Value.Op.Length).Trim();
                var leftExpr = ParseExpression(left, inputParam);
                var rightExpr = ParseExpression(right, inputParam);
                return op.Value.Op switch
                {
                    "==" => Expression.Equal(leftExpr, rightExpr),
                    "!=" => Expression.NotEqual(leftExpr, rightExpr),
                    "<=" => Expression.LessThanOrEqual(leftExpr, rightExpr),
                    ">=" => Expression.GreaterThanOrEqual(leftExpr, rightExpr),
                    "<" => Expression.LessThan(leftExpr, rightExpr),
                    ">" => Expression.GreaterThan(leftExpr, rightExpr),
                    _ => throw new InvalidOperationException("Unknown comparison operator: " + op.Value.Op)
                };
            }

            if (expr.StartsWith("!"))
            {
                var inner = expr.Substring(1).Trim();
                return Expression.Not(ParseExpression(inner, inputParam));
            }

            if (expr.StartsWith("(") && expr.EndsWith(")"))
            {
                return ParseExpression(expr.Substring(1, expr.Length - 2).Trim(), inputParam);
            }

            throw new InvalidOperationException("Unable to parse rule expression: " + expr);
        }

        private (int Index, string Op)? FindTopLevelOperator(string expr, string[] ops)
        {
            int depth = 0;
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (depth == 0)
                {
                    foreach (var op in ops)
                    {
                        if (i + op.Length <= expr.Length && expr.Substring(i, op.Length) == op)
                            return (i, op);
                    }
                }
            }
            return null;
        }

        public void Dispose()
        {
            compiledRules.Clear();
        }
    }

    /// <summary>
    /// Result of evaluating a single rule.
    /// <para>Результат вычисления одного правила.</para>
    /// </summary>
    public class RuleResult
    {
        public string RuleName { get; set; } = "";
        public bool IsSuccess { get; set; }
        public string SuccessEvent { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public List<RuleAction> Actions { get; set; } = new List<RuleAction>();
    }
}
