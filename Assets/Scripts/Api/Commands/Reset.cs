/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using SimpleJSON;
using System.Threading.Tasks;

namespace Simulator.Api.Commands
{
    class Reset : IDistributedCommand
    {
        public string Name => "simulator/reset";

        private static async Task ResetAsync(Reset sourceCommand)
        {
            var api = ApiManager.Instance;
            try
            {
                await api.Reset();
            }
            catch (Exception ex)
            {
                // Nothing awaits this task: answer the client, or it waits forever.
                UnityEngine.Debug.LogException(ex);
                api.SendError(sourceCommand, ex.Message);
                return;
            }
            ApiManager.Instance.SendResult(sourceCommand);
        }

        public void Execute(JSONNode args)
        {
            var nonBlockingTask = ResetAsync(this);
        }
    }
}
