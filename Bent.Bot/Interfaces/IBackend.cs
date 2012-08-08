﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bent.Bot.Common;

namespace Bent.Bot
{
    public interface IBackend : IDisposable, IObservable<MessageData>
    {
        Task ConnectAsync();
        Task DisconnectAsync();

        Task SendMessageAsync(IAddress address, string body);
    }
}
