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

namespace Papercut.Services
{
    using System;
    using System.ComponentModel;
    using System.Reactive.Concurrency;
    using System.Reactive.Linq;
    using System.Runtime.CompilerServices;

    using Papercut.Core.Annotations;
    using Papercut.Core.Events;
    using Papercut.Core.Network;
    using Papercut.Events;
    using Papercut.Properties;

    using Serilog;

    public class SmtpServerCoordinator : IHandleEvent<PapercutClientReadyEvent>,
        IHandleEvent<PapercutClientExitEvent>,
        IHandleEvent<SettingsUpdatedEvent>,
        INotifyPropertyChanged
    {
        readonly ILogger _logger;

        readonly IPublishEvent _publishEvent;

        readonly Func<ServerProtocolType, IServer> _serverFactory;

        readonly Lazy<IServer> _smtpServer;

        bool _smtpServerEnabled = true;

        public SmtpServerCoordinator(
            Func<ServerProtocolType, IServer> serverFactory,
            ILogger logger,
            IPublishEvent publishEvent)
        {
            _serverFactory = serverFactory;
            _smtpServer = new Lazy<IServer>(() => _serverFactory(ServerProtocolType.Smtp));
            _logger = logger;
            _publishEvent = publishEvent;
        }

        public bool SmtpServerEnabled
        {
            get { return _smtpServerEnabled; }
            set
            {
                if (value.Equals(_smtpServerEnabled)) return;
                _smtpServerEnabled = value;
                OnPropertyChanged();
            }
        }

        public void Handle(PapercutClientExitEvent @event)
        {
            _smtpServer.Value.Stop();
        }

        public void Handle(PapercutClientReadyEvent @event)
        {
            if (SmtpServerEnabled) ListenSmtpServer();

            PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == "StmpServerEnabled")
                {
                    if (SmtpServerEnabled && !_smtpServer.Value.IsActive)
                        ListenSmtpServer();
                    else if (!SmtpServerEnabled && _smtpServer.Value.IsActive)
                        _smtpServer.Value.Stop();
                }
            };
        }

        public void Handle(SettingsUpdatedEvent @event)
        {
            if (SmtpServerEnabled) ListenSmtpServer();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void ListenSmtpServer()
        {
            _smtpServer.Value.BindObservable(
                Settings.Default.IP,
                Settings.Default.Port,
                TaskPoolScheduler.Default)
                .DelaySubscription(TimeSpan.FromMilliseconds(500)).Retry(5)
                .Subscribe(
                    b => { },
                    ex =>
                    {
                        _logger.Warning(
                            ex,
                            "Failed to bind SMTP to the {Address} {Port} specified. The port may already be in use by another process.",
                            Settings.Default.IP,
                            Settings.Default.Port);

                        _publishEvent.Publish(new SmtpServerBindFailedEvent());
                    },
                    () =>
                    _publishEvent.Publish(
                        new SmtpServerBindEvent(Settings.Default.IP, Settings.Default.Port)));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}