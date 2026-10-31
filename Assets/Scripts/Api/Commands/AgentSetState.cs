/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using SimpleJSON;
using UnityEngine;
using UnityEngine.AI;

namespace Simulator.Api.Commands
{
    class AgentSetState : ICommand
    {
        public string Name => "agent/state/set";

        public void Execute(JSONNode args)
        {
            var uid = args["uid"].Value;
            var api = ApiManager.Instance;

            if (api.Agents.TryGetValue(uid, out GameObject obj))
            {
                var position = args["state"]["transform"]["position"].ReadVector3();
                var rotation = args["state"]["transform"]["rotation"].ReadVector3();
                var velocity = args["state"]["velocity"].ReadVector3();
                var angular_velocity = args["state"]["angular_velocity"].ReadVector3();

                var ped = obj.GetComponent<PedestrianController>();
                if (ped == null)
                {
                    var tr = obj.transform;
                    var rb = obj.GetComponent<Rigidbody>();
                    var body = rb == null ? AgentGetState.RootArticulation(obj) : null;
                    if (body != null)
                    {
                        // An articulation does not follow its parent transform: teleport its root body along with it.
                        var offset = Quaternion.Inverse(tr.rotation) * (body.transform.position - tr.position);
                        var turn = Quaternion.Inverse(tr.rotation) * body.transform.rotation;
                        tr.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                        body.TeleportRoot(tr.position + tr.rotation * offset, tr.rotation * turn);
                        body.velocity = velocity;
                        body.angularVelocity = angular_velocity;
                    }
                    else
                    {
                        tr.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                        if (rb != null)
                        {
                            rb.velocity = velocity;
                            rb.angularVelocity = angular_velocity;
                        }
                    }
                }
                else
                {
                    var agent = ped.GetComponent<NavMeshAgent>();
                    agent.Warp(position);
                    agent.transform.rotation = Quaternion.Euler(rotation);
                    agent.velocity = velocity;
                }

                api.SendResult(this);
            }
            else
            {
                api.SendError(this, $"Agent '{uid}' not found");
            }
        }
    }
}
