// Copyright (c) ArchScada. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Scada.Server.Modules.ModRuleEngine.Logic
{
    /// <summary>
    /// Rule configuration loaded from Rules.json.
    /// <para>Конфигурация правила, загружаемая из Rules.json.</para>
    /// </summary>
    public class RuleConfig
    {
        public string RuleName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Expression { get; set; } = "";
        public string RuleExpressionType { get; set; } = "LambdaExpression";
        public string SuccessEvent { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int MinIntervalMs { get; set; }
        public List<ChannelInput> ChannelInputs { get; set; } = new List<ChannelInput>();
        public List<RuleAction> Actions { get; set; } = new List<RuleAction>();

        public static List<RuleConfig> Load(string fileName)
        {
            var rules = new List<RuleConfig>();
            if (!File.Exists(fileName))
                return rules;
            try
            {
                var json = File.ReadAllText(fileName);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<RuleConfigJson>>(json);
                if (list == null) return rules;
                foreach (var item in list)
                {
                    rules.Add(new RuleConfig
                    {
                        RuleName = item.RuleName,
                        Description = item.Description,
                        Expression = item.Expression,
                        RuleExpressionType = item.RuleExpressionType,
                        SuccessEvent = item.SuccessEvent,
                        ErrorMessage = item.ErrorMessage,
                        Enabled = item.Enabled,
                        MinIntervalMs = item.MinIntervalMs,
                        ChannelInputs = item.ChannelInputs ?? new List<ChannelInput>(),
                        Actions = item.Actions ?? new List<RuleAction>()
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Failed to load rules: " + ex.Message, ex);
            }
            return rules;
        }
    }

    internal class RuleConfigJson
    {
        public string RuleName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Expression { get; set; } = "";
        public string RuleExpressionType { get; set; } = "LambdaExpression";
        public string SuccessEvent { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int MinIntervalMs { get; set; }
        public List<ChannelInput> ChannelInputs { get; set; } = new List<ChannelInput>();
        public List<RuleAction> Actions { get; set; } = new List<RuleAction>();
    }
}