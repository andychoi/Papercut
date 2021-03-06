﻿// Papercut
// 
// Copyright © 2008 - 2012 Ken Robertson
// Copyright © 2013 - 2015 Jaben Cargman
//  
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//  
// http://www.apache.org/licenses/LICENSE-2.0
//  
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Papercut.Core.Network
{
    using System;
    using System.IO;

    using Papercut.Core.Events;
    using Papercut.Core.Helper;

    using Serilog;

    public enum ProtocolCommandType
    {
        NoOp = 0,

        Publish = 1,

        Exchange = 2
    }

    public class PapercutProtocolRequest
    {
        public ProtocolCommandType CommandType { get; set; }

        public Type Type { get; set; }

        public int ByteSize { get; set; }
    }

    public class PapercutProtocol : StringCommandProtocol
    {
        readonly IPublishEvent _publishEvent;

        public PapercutProtocol(ILogger logger, IPublishEvent publishEvent)
            : base(logger)
        {
            _publishEvent = publishEvent;
        }

        public Connection Connection { get; protected set; }

        public override void Begin(Connection connection)
        {
            Connection = connection;
            _logger.ForContext("ConnectionId", Connection.Id);
            Connection.SendLine("PAPERCUT");
        }

        protected override void ProcessRequest(string incomingRequest)
        {
            try
            {
                var request = incomingRequest.FromJson<PapercutProtocolRequest>();

                _logger.Verbose("Incoming Request Received {@Request}", request);

                Connection.Send("ACK").Wait();

                if (request.CommandType.IsAny(ProtocolCommandType.Publish, ProtocolCommandType.Exchange))
                {
                    // read the rest of the object...
                    var @event = Connection.Client.ReadObj(request.Type, request.ByteSize);
                    _logger.Information("Publishing Event Received {@Event} from Remote", @event);

                    _publishEvent.PublishObject(@event, request.Type);

                    if (request.CommandType == ProtocolCommandType.Exchange)
                    {
                        // send response back...
                        _logger.Information("Exchanging Event {@Event} -- Pushing to Remote", @event);
                        Connection.Send("REPLY").Wait();
                        Connection.SendLine(@event.ToJson()).Wait();
                    }
                }
            }
            catch (IOException e)
            {
                _logger.Error(e, "IOException received. Closing this connection.");
                Connection.Close();
            }
        }
    }
}