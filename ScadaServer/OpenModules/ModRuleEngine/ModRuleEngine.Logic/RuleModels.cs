// Copyright (c) ArchScada. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Scada.Server.Modules.ModRuleEngine.Logic
{
    /// <summary>
    /// Input object presented to the rule engine on each evaluation.
    /// <para>Входной объект, передаваемый в модуль правил при каждом вычислении.</para>
    /// </summary>
    public class RuleInput
    {
        /// <summary>
        /// Channel values keyed by the alias configured in the rule.
        /// <para>Значения каналов, ключевые по псевдониму, настроенному в правиле.</para>
        /// </summary>
        public Dictionary<string, double> Channels { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Channel statuses keyed by the alias configured in the rule.
        /// <para>Статусы каналов, ключевые по псевдониму, настроенному в правиле.</para>
        /// </summary>
        public Dictionary<string, int> Statuses { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Timestamp of the data slice being evaluated (UTC).
        /// <para>Временная метка среза данных, вычисляемого в данный момент (UTC).</para>
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Mapping between a friendly alias used in a rule expression and a SCADA channel number.
    /// <para>Соответствие между дружелюбным псевдонимом, используемым в выражении правила, и номером канала SCADA.</para>
    /// </summary>
    public class ChannelInput
    {
        /// <summary>
        /// Friendly alias referenced in the rule expression.
        /// <para>Дружелюбное имя, используемое в выражении правила.</para>
        /// </summary>
        public string Alias { get; set; } = "";

        /// <summary>
        /// SCADA channel number.
        /// <para>Номер канала SCADA.</para>
        /// </summary>
        public int CnlNum { get; set; }
    }

    /// <summary>
    /// Action executed when a rule evaluates to true.
    /// <para>Действие, выполняемое когда правило вычисляется как истинное.</para>
    /// </summary>
    public class RuleAction
    {
        /// <summary>
        /// Action type: RaiseEvent, SendCommand or WriteData.
        /// <para>Тип действия: RaiseEvent, SendCommand или WriteData.</para>
        /// </summary>
        public string Type { get; set; } = "RaiseEvent";

        /// <summary>
        /// Event severity (used by RaiseEvent).
        /// <para>Тяжесть события (используется RaiseEvent).</para>
        /// </summary>
        public int Severity { get; set; } = 3;

        /// <summary>
        /// Event/command text.
        /// <para>Текст события/команды.</para>
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Target channel number (used by SendCommand and WriteData).
        /// <para>Целевой номер канала (используется SendCommand и WriteData).</para>
        /// </summary>
        public int CnlNum { get; set; }

        /// <summary>
        /// Command value (used by SendCommand).
        /// <para>Значение команды (используется SendCommand).</para>
        /// </summary>
        public double CmdVal { get; set; }

        /// <summary>
        /// Command data bytes (used by SendCommand).
        /// <para>Байты данных команды (используется SendCommand).</para>
        /// </summary>
        public byte[] CmdData { get; set; }
    }
}