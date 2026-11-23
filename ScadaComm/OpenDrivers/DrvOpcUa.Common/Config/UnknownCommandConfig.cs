// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace Scada.Comm.Drivers.DrvOpcUa.Config
{
    /// <summary>
    /// Represents a configuration of an unknown or unsupported OPC UA command type.
    /// <para>Представляет конфигурацию неизвестного или неподдерживаемого типа команды OPC UA.</para>
    /// </summary>
    public class UnknownCommandConfig : CommandConfig
    {
        private readonly CommandType _cmdType;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// <param name="cmdType">The unsupported command type.</param>
        public UnknownCommandConfig(CommandType cmdType)
        {
            _cmdType = cmdType;
        }

        /// <summary>
        /// Gets the command type.
        /// </summary>
        public override CommandType CmdType => _cmdType;

        /// <summary>
        /// Loads the configuration from the XML node.
        /// </summary>
        public override void LoadFromXml(XmlElement xmlElem)
        {
            base.LoadFromXml(xmlElem);
        }

        /// <summary>
        /// Saves the configuration into the XML node.
        /// </summary>
        public override void SaveToXml(XmlElement xmlElem)
        {
            base.SaveToXml(xmlElem);
        }
    }
}