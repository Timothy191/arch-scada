// Copyright (c) ArchScada. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Const;
using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Lang;
using Scada.Log;
using Scada.Protocol;
using Scada.Server.Lang;
using Scada.Server.Modules.ModRuleEngine.Logic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Scada.Server.Modules.ModRuleEngine.Logic
{
    /// <summary>
    /// Implements the server module logic for the rule engine.
    /// <para>Реализует логику серверного модуля модуля правил.</para>
    /// </summary>
    public class ModRuleEngineLogic : ModuleLogic
    {
        private const string LogFileName = ModuleUtils.ModuleCode + ".log";
        private const string RulesFileName = "Rules.json";

        private readonly LogFile moduleLog;
        private ModuleConfig moduleConfig;
        private List<RuleConfig> rules;
        private RuleEngineService ruleEngine;

        private bool fatalError;
        private volatile bool terminated;

        public ModRuleEngineLogic(IServerContext serverContext)
            : base(serverContext)
        {
            moduleLog = new LogFile(LogFormat.Simple)
            {
                FileName = Path.Combine(serverContext.AppDirs.LogDir, LogFileName),
                CapacityMB = serverContext.AppConfig.GeneralOptions.MaxLogSize
            };
            moduleConfig = new ModuleConfig();
            rules = new List<RuleConfig>();
            ruleEngine = new RuleEngineService(rules);

            fatalError = false;
            terminated = false;
        }

        public override string Code => ModuleUtils.ModuleCode;

        public override void OnServiceStart()
        {
            moduleLog.WriteBreak();
            moduleLog.WriteAction(ServerPhrases.StartModule, Code, Version);

            // Load module configuration.
            var configFile = Path.Combine(ServerContext.AppDirs.ConfigDir, "ModRuleEngine.xml");
            moduleConfig = ModuleConfig.Load(configFile);

            // Load rules.
            var rulesFile = Path.Combine(ServerContext.AppDirs.ConfigDir, RulesFileName);
            try
            {
                rules = RuleConfig.Load(rulesFile);
            }
            catch (Exception ex)
            {
                moduleLog.WriteError("Failed to load rules: " + ex.Message);
                rules = new List<RuleConfig>();
            }

            // Compile the rule engine.
            try
            {
                ruleEngine = new RuleEngineService(rules);
                ruleEngine.Compile();
                moduleLog.WriteAction("Rule engine compiled. {0} rule(s) loaded.", rules.Count(r => r.Enabled));
            }
            catch (Exception ex)
            {
                moduleLog.WriteError("Failed to compile rule engine: " + ex.Message);
                fatalError = true;
            }

            if (fatalError)
            {
                Log.WriteError(ServerPhrases.ModuleMessage, Code, "Rule engine initialization failed.");
                moduleLog.WriteError(CommonPhrases.ExecutionImpossible);
            }
        }

        public override void OnServiceReady()
        {
            if (!fatalError)
            {
                moduleLog.WriteAction(ServerPhrases.ModuleMessage, Code, "Rule engine service is ready.");
            }
        }

        public override void OnServiceStop()
        {
            terminated = true;
            ruleEngine?.Dispose();
            ruleEngine = null;
            moduleLog.WriteAction(ServerPhrases.StopModule, Code);
            moduleLog.WriteBreak();
        }

        public override void OnCurrentDataProcessed(Slice slice)
        {
            if (fatalError || terminated || ruleEngine == null || rules.Count == 0 || slice.Length == 0)
                return;

            var cnlLookup = new Dictionary<int, CnlData>();
            for (int i = 0; i < slice.Length; i++)
                cnlLookup[slice.CnlNums[i]] = slice.CnlData[i];

            foreach (var rule in rules.Where(r => r.Enabled))
            {
                if (!rule.ChannelInputs.Any(ci => cnlLookup.ContainsKey(ci.CnlNum)))
                    continue;

                var input = BuildInput(slice, cnlLookup, rule);
                var results = ruleEngine.Evaluate(input);

                foreach (var result in results)
                {
                    if (result.IsSuccess)
                    {
                        moduleLog.WriteAction("Rule '{0}' fired: {1}", rule.RuleName, result.SuccessEvent);
                        ExecuteActions(result);
                    }
                    else if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        moduleLog.WriteWarning("Rule '{0}' error: {1}", rule.RuleName, result.ErrorMessage);
                    }
                }
            }
        }

        private RuleInput BuildInput(Slice slice, Dictionary<int, CnlData> cnlLookup, RuleConfig rule)
        {
            var input = new RuleInput { Timestamp = slice.Timestamp };
            foreach (var ci in rule.ChannelInputs)
            {
                if (cnlLookup.TryGetValue(ci.CnlNum, out var cnlData))
                {
                    input.Channels[ci.Alias] = cnlData.Val;
                    input.Statuses[ci.Alias] = cnlData.Stat;
                }
                else
                {
                    input.Channels[ci.Alias] = double.NaN;
                    input.Statuses[ci.Alias] = (int)CnlStatusID.Undefined;
                }
            }
            return input;
        }

        private void ExecuteActions(RuleResult result)
        {
            foreach (var action in result.Actions)
            {
                try
                {
                    switch (action.Type)
                    {
                        case "RaiseEvent":
                            if (moduleConfig.RaiseEvents) RaiseEvent(action);
                            break;
                        case "SendCommand":
                            if (moduleConfig.SendCommands) SendCommand(action);
                            break;
                        case "WriteData":
                            WriteData(action);
                            break;
                        default:
                            moduleLog.WriteWarning("Unknown rule action type: {0}", action.Type);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    moduleLog.WriteError("Failed to execute rule action '{0}': {1}", action.Type, ex.Message);
                }
            }
        }

        private void RaiseEvent(RuleAction action)
        {
            var ev = new Event
            {
                Timestamp = DateTime.UtcNow,
                CnlNum = action.CnlNum,
                Severity = action.Severity,
                AckRequired = action.Severity >= 3,
                Text = action.Text,
                TextFormat = EventTextFormat.Full
            };
            ServerContext.WriteEvent(1, ev);
            moduleLog.WriteAction("Event raised: {0} (severity {1})", action.Text, action.Severity);
        }

        private void SendCommand(RuleAction action)
        {
            var cmd = new TeleCommand(action.CnlNum, action.CmdVal, 0)
            {
                CmdCode = "RuleEngine",
                CmdData = action.CmdData
            };
            ServerContext.SendCommand(cmd);
            moduleLog.WriteAction("Command sent to channel {0}, value {1}", action.CnlNum, action.CmdVal);
        }

        private void WriteData(RuleAction action)
        {
            var cnlData = new CnlData(action.CmdVal, (int)CnlStatusID.Defined);
            ServerContext.WriteCurrentData(action.CnlNum, cnlData);
            moduleLog.WriteAction("Data written to channel {0}, value {1}", action.CnlNum, action.CmdVal);
        }

        public override void OnCommand(TeleCommand command, CommandResult commandResult)
        {
            if (!string.IsNullOrEmpty(command.CmdCode) && command.CmdCode == "RuleEngine")
            {
                commandResult.TransmitToClients = false;
                var args = command.GetCmdDataArgs();
                var subCmd = args.GetValueAsString("cmd", "");

                if (subCmd == "Reload")
                {
                    try
                    {
                        var rulesFile = Path.Combine(ServerContext.AppDirs.ConfigDir, RulesFileName);
                        var newRules = RuleConfig.Load(rulesFile);
                        var newEngine = new RuleEngineService(newRules);
                        newEngine.Compile();

                        rules.Clear();
                        rules.AddRange(newRules);
                        ruleEngine?.Dispose();
                        ruleEngine = newEngine;

                        moduleLog.WriteAction("Rules reloaded. {0} rule(s) active.", rules.Count(r => r.Enabled));
                    }
                    catch (Exception ex)
                    {
                        moduleLog.WriteError("Rule reload failed: " + ex.Message);
                    }
                }
                else
                {
                    moduleLog.WriteWarning("Unknown module command: {0}", subCmd);
                }
            }
        }
    }
}