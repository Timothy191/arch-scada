// Copyright (c) ArchScada. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Xml;

namespace Scada.Server.Modules.ModRuleEngine.Logic
{
    /// <summary>
    /// General module options loaded from ModRuleEngine.xml.
    /// <para>Общие параметры модуля, загружаемые из ModRuleEngine.xml.</para>
    /// </summary>
    public class ModuleConfig
    {
        public int PollIntervalMs { get; set; }
        public bool RaiseEvents { get; set; } = true;
        public bool SendCommands { get; set; } = true;

        public static ModuleConfig Load(string fileName)
        {
            var config = new ModuleConfig();
            if (!File.Exists(fileName))
                return config;
            try
            {
                var doc = new XmlDocument();
                doc.Load(fileName);
                var root = doc.DocumentElement;
                if (root == null) return config;
                var general = root["GeneralOptions"];
                if (general != null)
                {
                    if (int.TryParse(general["PollIntervalMs"]?.InnerText, out var poll))
                        config.PollIntervalMs = poll;
                    if (bool.TryParse(general["RaiseEvents"]?.InnerText, out var raise))
                        config.RaiseEvents = raise;
                    if (bool.TryParse(general["SendCommands"]?.InnerText, out var send))
                        config.SendCommands = send;
                }
            }
            catch
            {
                // Return defaults on malformed config.
            }
            return config;
        }
    }
}