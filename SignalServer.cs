using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Diagnostics;
using VRage.Game.Entity;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Character;
using VRage.Game.ModAPI;
using VRageMath;
using NLog;
using Torch;
using Torch.API;
using Torch.Commands;
using Torch.Commands.Permissions;
using WebSocketSharp.Server;

namespace GalacticComms
{
    public class SignalServerConfig : ViewModel
    {
        private int _Port = 3456;

        public int Port { get => _Port; set => SetValue(ref _Port, value); }
    }

    public class SignalServerBehaviour : WebSocketBehavior
    {
        // Empty ATM. There isn't any form of reply, but might add some in the future
    }

    struct QualityPacket
    {
        public char packetId;
        public ulong fromId;
        public ulong toId;
        public float quality;
    }

    struct HeartbeatPacket
    {
        public char packetId;
    }


    [Category("signal")]
    public class SignalServerCommands : CommandModule
    {
        [Command("strength", "Displays your received signal strengths.")]
        [Permission(MyPromoteLevel.None)]
        public void DisplayStrengths()
        {
            try
            {
                var plugin = Context.Plugin as SignalServerPlugin;
                if (Context.Player is MyPlayer player)
                {
                    plugin.DebugWalk = true;
                    var signals = plugin.GetPlayerReceivedSignals(player);
                    plugin.DebugWalk = false;
                    var response = new StringBuilder("Your current signals:\n");
                    
                    foreach (var pair in signals)
                    {
                        var entity = MyEntities.GetEntityByIdOrDefault(pair.Key);
                        string name = "No Entity Found";
                        if (entity == null)
                        {
                            name = "No Entity Found";
                        } else if (entity is MyCubeGrid grid)
                        {
                            name = grid.DisplayName;
                        } else if (entity is MyCharacter character) {
                            name = character.DisplayName;
                        }  else
                        {
                            name = entity.GetType().Name;
                        }
                        response.Append($"[{pair.Key}] {name}: {pair.Value:P}\n");
                    }
                    Context.Respond(response.ToString());
                }
                else
                {
                    Context.Respond("Can't get signals for the server console!");
                }
            } catch (Exception e)
            {
                Context.Respond(e.ToString());
            }
        }

        [Command("performance", "Displays the performance information from the previous few update cycles.")]
        [Permission(MyPromoteLevel.None)]
        public void DisplayPerformance()
        {
            try
            {
                var plugin = Context.Plugin as SignalServerPlugin;
                double average = 0;
                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                for (int i = 0; i < plugin.performanceLogs.Length; i++)
                {
                    double record = plugin.performanceLogs[i];
                    min = min > record ? record : min;
                    max = max < record ? record : max;
                    average += record;
                }
                average /= plugin.performanceLogs.Length;
                Context.Respond($"Stats for the past {plugin.performanceLogs.Length} update cycles:\nMin: {min:N3}ms\nAvg: {average:N3}\nMax: {max:N3}");
            }
            catch (Exception e)
            {
                Context.Respond(e.ToString());
            }
        }

        [Command("switch", "Switches between the two methods of calculating signals.")]
        [Permission(MyPromoteLevel.None)]
        public void SwitchMethod()
        {
            try
            {
                var plugin = Context.Plugin as SignalServerPlugin;
                plugin.useNewMethod = !plugin.useNewMethod;
                Context.Respond($"Now using the {(plugin.useNewMethod?"new":"old")} method.");
            }
            catch (Exception e)
            {
                Context.Respond(e.ToString());
            }
        }

        [Command("debug", "Displays the registered debug grids.")]
        [Permission(MyPromoteLevel.None)]
        public void DisplayDebugGrids()
        {
            try
            {
                if (Context.Player is MyPlayer player)
                {
                var plugin = Context.Plugin as SignalServerPlugin;
                var response = new StringBuilder("Current Debug Grids:\n");
                foreach (var grid in plugin.debugGrids) response.Append($"{grid.DisplayName} - {Vector3.Distance(grid.PositionComp.GetPosition(), player.GetPosition()):N3}m");
                Context.Respond(response.ToString());
                } else
                {
                    Context.Respond("Cannot get debug grids on server.");
                }

            }
            catch (Exception e)
            {
                Context.Respond(e.ToString());
            }
        }

        [Command("ship", "Gets the ship the player is currently in.")]
        [Permission(MyPromoteLevel.None)]
        public void GetPlayerShip()
        {
            try
            {
                if (Context.Player is MyPlayer player)
                {
                    var broadcasters = new HashSet<MyDataBroadcaster>();
                    MyAntennaSystem.Static.GetEntityBroadcasters(player.Character, ref broadcasters, player.Identity.IdentityId);
                    StringBuilder response = new StringBuilder();
                    foreach (MyDataBroadcaster b in broadcasters)
                    {
                        response.Append(b.Info.Name);
                        response.AppendLine();
                    }
                    Context.Respond(response.ToString());
                }
                else
                {
                    Context.Respond("Cannot get debug grids on server.");
                }

            }
            catch (Exception e)
            {
                Context.Respond(e.ToString());
            }
        }
    }

    public class SignalServerPlugin : TorchPluginBase
    {
        private Persistent<SignalServerConfig> _config;
        private WebSocketServer wssv;
        private WebSocketServiceHost host;
        private HashSet<MyDataBroadcaster> _dataBroadcasters = new HashSet<MyDataBroadcaster>();
        private HashSet<MyDataReceiver> _dataRecievers = new HashSet<MyDataReceiver>();
        private Dictionary<ulong, double> _playerConnections = new Dictionary<ulong, double>();
        public SignalServerConfig Config => _config?.Data;
        public static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();
        public bool DebugWalk = false;

        public override void Init(ITorchBase torch)
        {
            Log.Info("Initialising Galctic Comms Torch Plugin...");
            base.Init(torch);
            Log.Info("Setting up configuration...");
            SetupConfig();
            Log.Info("Starting up voice server communication...");
            wssv = new WebSocketServer(IPAddress.Parse("127.0.0.1"), Config.Port, false);
            wssv.AddWebSocketService<SignalServerBehaviour>("/");
            wssv.Log.Level = WebSocketSharp.LogLevel.Warn;
            wssv.Log.Output = (data, msg) => Log.Warn($"Websocket Log [{data.Level}]: {data.Message}");
            wssv.WebSocketServices.TryGetServiceHost("/", out host);
            wssv.Start();
            Log.Info("Galactic Comms Initialisation COMPLETE");

        }

        public HashSet<MyCubeGrid> debugGrids = new HashSet<MyCubeGrid>();


        byte[] ToBytes<T>(T data)
        {
            int size = Marshal.SizeOf(data);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(data, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        private void SendQualityPacket(ulong fromId, ulong toId, float quality)
        {
            QualityPacket packet;
            packet.packetId = 'Q';
            packet.fromId = fromId;
            packet.toId = toId;
            packet.quality = quality;
            SendPacket(packet);
        }

        private void SendHeartbeatPacket()
        {
            HeartbeatPacket packet;
            packet.packetId = 'H';
            SendPacket(packet);
        }

        private void SendPacket<T>(T packet)
        {
            host.Sessions.Broadcast(ToBytes<T>(packet));
        }

        public double GetSignalStrength(MyDataBroadcaster from, MyDataReceiver to)
        {
            if (from is MyRadioBroadcaster fromRadio && to is MyRadioReceiver toRadio)
            {
                double dist2 = Vector3D.DistanceSquared(fromRadio.BroadcastPosition, toRadio.Entity.PositionComp.GetPosition());
                double q = 1 - Math.Max(0.0, Math.Min(1.0, dist2 / (fromRadio.BroadcastRadius * fromRadio.BroadcastRadius)));
                return q * q;
            } else
            {
                return 1.0;
            }
        }

        private class NetworkConnection
        {
            public MyCubeGrid from;
            public MyCubeGrid to;
            public double strength;

            public NetworkConnection(MyCubeGrid from, MyCubeGrid to, double strength)
            {
                this.from = from;
                this.to = to;
                this.strength = strength;
            }

            public override string ToString()
            {
                return $"{from?.Name} => [{strength:P}] => {to?.Name}";
            }

            public class Comparer : Comparer<NetworkConnection>
            {
                public override int Compare(NetworkConnection x, NetworkConnection y)
                {
                    return x.strength.CompareTo(y.strength);
                }
            }
        }

        private Dictionary<MyCubeGrid, double> gridC = new Dictionary<MyCubeGrid, double>();
        private Dictionary<MyCubeGrid, NetworkConnection> gridE = new Dictionary<MyCubeGrid, NetworkConnection>();
        private HashSet<MyCubeGrid> gridQ = new HashSet<MyCubeGrid>();
        private HashSet<MyCubeGrid> gridF = new HashSet<MyCubeGrid>();

        private double BacktraceSignal(MyDataReceiver receiver)
        {
            if (DebugWalk) Log.Info(string.Join("\n", gridE.Select(kv => $"{kv.Key?.Name} : {kv.Value}").ToArray()));
            MyFunctionalBlock block = (receiver.Entity as MyFunctionalBlock);
            MyCubeGrid grid = block?.CubeGrid;
            double result = 1.0;
            StringBuilder path = new StringBuilder();
            while (grid != null)
            {
                if (DebugWalk) path.Append($" => {grid.DisplayName}");
                result *= gridE.GetValueOrDefault(grid)?.strength ?? 0.0;
                grid = gridE.GetValueOrDefault(grid)?.to;
            }
            if (DebugWalk) Log.Info($"Backtraced {path}");
            return result;
        }


        private void UpdateMinTreeEdges(MyDataReceiver receiver, HashSet<MyDataBroadcaster> broadcasters)
        {
            if (DebugWalk) Log.Info($"Updating from {receiver.Entity.EntityId}, with {broadcasters.Count} broadcasters in range.");
            MyCubeGrid rGrid = (receiver.Entity as MyFunctionalBlock)?.CubeGrid;
            foreach (MyDataBroadcaster broadcaster in broadcasters)
            {
                if (DebugWalk) Log.Info($"Checking broadcaster {broadcaster.Info.Name}:");
                if (broadcaster.Entity is MyEntity entity)
                {
                    if (DebugWalk) Log.Info($"Had entity");
                    double signal2 = GetSignalStrength(broadcaster, receiver);
                    if (signal2 <= 0) continue;
                    //double cSignal2 = (rGrid != null ? gridCC.GetValueOrDefault(rGrid, 1.0) : 1) * signal2;
                    if (DebugWalk) Log.Info($"Signals( Immediate: {signal2:P})");
                    if (broadcaster.Info.Name.Contains("[DEBUG RADIO]"))
                    {
                        ulong fakePlayerId = unchecked((ulong)broadcaster.Entity.EntityId);
                        double playerCurrentSignal2 = _playerConnections.GetValueOrDefault(fakePlayerId, 0.0f);
                        double cSignal2 = BacktraceSignal(receiver) * signal2;
                        if (DebugWalk) Log.Info($"Tracking fake player {fakePlayerId}[{playerCurrentSignal2:P} => {cSignal2:P}]");
                        if (cSignal2 > playerCurrentSignal2) _playerConnections[fakePlayerId] = cSignal2;
                    }
                    if (entity is MyFunctionalBlock block && !gridF.Contains(block.CubeGrid))
                    {
                        var grid = block.CubeGrid;
                        if (DebugWalk) Log.Info($"Tracking Grid");
                        gridQ.Add(MyAntennaSystem.Static.GetLogicalGroupRepresentative(grid));
                        double current = gridC.GetValueOrDefault(grid, 0.0);
                        if (current < signal2)
                        {
                            if (DebugWalk) Log.Info($"Found better signal connection");
                            gridC[grid]= signal2;
                            //gridCC[grid]= cSignal2;
                            gridE[grid]= new NetworkConnection(grid, rGrid, signal2);
                        }
                    }
                    else if (entity is MyCharacter character)
                    {
                        if (DebugWalk) Log.Info($"Tracking real player");
                        MyPlayer.PlayerId playerIdentity;
                        if (character.GetPlayerId(out playerIdentity))
                        {
                            ulong playerId = playerIdentity.SteamId;
                            double playerCurrentSignal2 = _playerConnections.GetValueOrDefault(playerId, 0.0f);
                            double cSignal2 = BacktraceSignal(receiver) * signal2;
                            if (DebugWalk) Log.Info($"Player online: {playerId}[{cSignal2:P}]");
                            if (cSignal2 > playerCurrentSignal2) _playerConnections[playerId] = cSignal2;
                        }
                    }
                }
            }
        }

        private void WalkNetwork(MyPlayer playerTo)
        {
            SortedSet<NetworkConnection> heap = new SortedSet<NetworkConnection>(new NetworkConnection.Comparer());

            gridC.Clear();
            gridE.Clear();
            gridQ.Clear();
            gridF.Clear();

            _dataRecievers.Clear();
            _dataBroadcasters.Clear();
            MyAntennaSystem.Static.GetEntityReceivers(playerTo.Character, ref _dataRecievers, playerTo.Identity.IdentityId);
            var receiver = _dataRecievers.Take(1).First();

            UpdateMinTreeEdges(receiver, receiver.BroadcastersInRange);


            while (gridQ.Count > 0)
            {
                MyCubeGrid grid = null;
                double currentMax = 0;
                foreach(MyCubeGrid candidate in gridQ)
                {
                    double cv = gridC.GetValueOrDefault(candidate, 0.0);
                    if (grid == null || cv > currentMax)
                    {
                        grid = candidate;
                        currentMax = cv;
                    }
                }
                if (grid != null)
                {
                    gridQ.Remove(grid);
                    gridF.Add(grid);
                    _dataRecievers.Clear();
                    _dataBroadcasters.Clear();
                    MyAntennaSystem.Static.GetEntityReceivers(grid, ref _dataRecievers, playerTo.Identity.IdentityId);
                    foreach (MyDataReceiver r in _dataRecievers)
                    {
                        var receiverEntity = r.Entity as MyFunctionalBlock;

                        if (receiverEntity != null) if (DebugWalk) Log.Info($"Checking reciever {receiverEntity.CustomName}");
                        UpdateMinTreeEdges(r, r.BroadcastersInRange);
                    }

                }
            }
        }

        public Dictionary<long, double> GetPlayerReceivedSignals(MyPlayer player)
        {
            var players = MySession.Static.Players.GetOnlinePlayers();
            HashSet<long> targets = new HashSet<long>();
            foreach (MyPlayer playerTarget in players) if (playerTarget.Character != null) targets.Add(playerTarget.Character.EntityId);
            foreach (MyCubeGrid grid in debugGrids) if (!grid.DisplayName.Contains("[DEBUG RADIO]")) debugGrids.Remove(grid);
            if (DebugWalk)
            {
                foreach (MyDataBroadcaster broadcaster in MyAntennaSystem.Static.GetAllRelayedBroadcasters(player.Character, player.Identity.IdentityId, false))
                {
                    if (MyAntennaSystem.Static.GetBroadcasterParentEntity(broadcaster) is MyCubeGrid grid)
                    {
                        if (grid.DisplayName.Contains("[DEBUG RADIO]")) debugGrids.Add(grid);
                    }
                }
            }
            foreach (MyCubeGrid grid in debugGrids) targets.Add(grid.EntityId);
            Dictionary<long, double> connections = BestPath(player, targets);
            stopwatch.Stop();

            foreach (var playerFrom in players)
            {
                if (!player.Id.SteamId.Equals(playerFrom.Id.SteamId)) if (playerFrom.Character != null) SendQualityPacket(playerFrom.Id.SteamId, player.Id.SteamId, (float)Math.Sqrt(connections.GetValueOrDefault(playerFrom.Character.EntityId, 0)));
            }
            foreach (var grid in debugGrids)
            {
                SendQualityPacket(unchecked((ulong)grid.EntityId), player.Id.SteamId, (float)Math.Sqrt(connections.GetValueOrDefault(grid.EntityId, 0)));
            }
            return connections;
        }

        public double[] performanceLogs = new double[2000];
        private int performanceIndex = 0;
        private Stopwatch stopwatch = new Stopwatch();
        public bool useNewMethod = true;

        private Stopwatch packetTimer = Stopwatch.StartNew();
        private Stopwatch heartbeatTimer = Stopwatch.StartNew();

        private struct NodeCostPair
        {
            public MyEntity entity;
            public double quality;
        }

        public Dictionary<long, double> BestPath(MyPlayer from, HashSet<long> targets)
        {
            Dictionary<long, double> results = new Dictionary<long, double>();
            NodeCostPair node = new NodeCostPair
            {
                entity = from.Character,
                quality = 1.0
            };
            LinkedList<NodeCostPair> frontier = new LinkedList<NodeCostPair>();
            frontier.AddFirst(node);
            HashSet<long> explored = new HashSet<long>();
            while (true)
            {
                if (frontier.Count <= 0) {
                    if (DebugWalk) Log.Info("Exauhsted Connections. Stopping.");
                    return results;
                }
                node = frontier.First.Value;
                frontier.RemoveFirst();
                if (DebugWalk)
                {
                    Log.Info($"Visiting: {node.entity.DisplayName} {node.quality}");
                }
                if (targets.Contains(node.entity.EntityId))
                {
                    if (DebugWalk) Log.Info($"Found target {node.entity.DisplayName}");
                    double current = results.GetValueOrDefault(node.entity.EntityId, 0);
                    if (node.quality > current)
                    {
                        if (DebugWalk) Log.Info($"Found better connection to {node.entity.DisplayName}: {node.quality}");
                        results[node.entity.EntityId] = node.quality;
                    }
                    if (results.Count >= targets.Count-1)
                    {
                        if (DebugWalk) Log.Info("Reached all targets. Stopping.");
                        return results;
                    }
                }
                explored.Add(node.entity.EntityId);
                _dataRecievers.Clear();
                MyAntennaSystem.Static.GetEntityReceivers(node.entity, ref _dataRecievers, from.Identity.IdentityId);
                foreach (var n in _dataRecievers)
                {
                    foreach (var b in n.BroadcastersInRange)
                    {
                        if (DebugWalk) Log.Info($"Checking {b.Info.Name}");
                        var quality = GetSignalStrength(b, n) * node.quality;
                        if (quality <= 0) continue;
                        var entity = MyAntennaSystem.Static.GetBroadcasterParentEntity(b);
                        if (entity is MyCubeGrid grid)
                        {
                            entity = MyAntennaSystem.Static.GetLogicalGroupRepresentative(grid);
                            if (entity.DisplayName.Contains("[DEBUG RADIO]"))
                            {
                                debugGrids.Add(entity as MyCubeGrid);
                            }
                        }
                        if (!explored.Contains(entity.EntityId))
                        {
                            if (DebugWalk) Log.Info("Was not previously explored.");
                            if (b.Entity is MyBeacon beacon)
                            {
                                _dataBroadcasters.Clear();
                                MyAntennaSystem.Static.GetEntityBroadcasters(entity, ref _dataBroadcasters, from.Identity.IdentityId);
                                foreach (var gridBroadcaster in _dataBroadcasters)
                                {
                                    var parent = MyAntennaSystem.Static.GetBroadcasterParentEntity(gridBroadcaster);
                                    if (targets.Contains(parent.EntityId))
                                    {
                                        if (DebugWalk) Log.Info($"Found target '{parent.DisplayName}' from beacon.");
                                        double current = results.GetValueOrDefault(parent.EntityId, 0);
                                        if (quality > current)
                                        {
                                            if (DebugWalk) Log.Info($"Found better connection to {parent.DisplayName}: {quality}");
                                            results[parent.EntityId] = quality;
                                            if (results.Count >= targets.Count - 1)
                                            {
                                                if (DebugWalk) Log.Info("Reached all targets. Stopping.");
                                                return results;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                bool inserted = false;
                                LinkedListNode<NodeCostPair> fNode = frontier.First;
                                if (fNode == null)
                                {
                                    frontier.AddFirst(new NodeCostPair { entity = entity, quality = quality });
                                    if (DebugWalk) Log.Info("Added to empty queue.");
                                }
                                else while (fNode != null)
                                    {
                                        if (!inserted)
                                        {
                                            if (fNode.Value.entity.EntityId == entity.EntityId)
                                            {
                                                if (fNode.Value.quality < quality)
                                                {
                                                    frontier.AddBefore(fNode, new NodeCostPair { entity = entity, quality = quality });
                                                    frontier.Remove(fNode);
                                                    inserted = true;
                                                    if (DebugWalk) Log.Info("Updated existing node.");
                                                }
                                                else
                                                {
                                                    if (DebugWalk) Log.Info("Node already present in queue in better position.");
                                                    break;
                                                }
                                            }
                                            else
                                            {
                                                if (fNode.Value.quality < quality)
                                                {
                                                    frontier.AddBefore(fNode, new NodeCostPair { entity = entity, quality = quality });
                                                    if (DebugWalk) Log.Info("Inserted into queue.");
                                                    inserted = true;
                                                }
                                            }
                                        }
                                        else if (fNode.Value.entity.EntityId == entity.EntityId)
                                        {
                                            frontier.Remove(fNode);
                                            if (DebugWalk) Log.Info("Removed old node from queue.");
                                            break;
                                        }
                                        fNode = fNode.Next;
                                    }
                                if (!inserted) frontier.AddLast(new NodeCostPair { entity = entity, quality = quality });
                            }
                        }
                        
                       
                    }
                }
            }
        }

        public override void Update()
        {
            base.Update();
            if (heartbeatTimer.ElapsedMilliseconds > 5000)
            {
                SendHeartbeatPacket();
                heartbeatTimer.Restart();
            }
            if (packetTimer.ElapsedMilliseconds > 500)
            {
                packetTimer.Restart();
                stopwatch.Reset();
                var players = MySession.Static.Players.GetOnlinePlayers();
                if (useNewMethod)
                {
                    HashSet<long> targets = new HashSet<long>();
                    foreach (MyPlayer player in players) if (player.Character != null) targets.Add(player.Character.EntityId);
                    foreach (MyCubeGrid grid in debugGrids) if (!grid.DisplayName.Contains("[DEBUG RADIO]")) debugGrids.Remove(grid);
                    foreach (MyCubeGrid grid in debugGrids) targets.Add(grid.EntityId);

                    foreach (var playerTo in players)
                    {
                        if (playerTo.Character != null)
                        {
                            stopwatch.Start();
                            Dictionary<long, double> connections = BestPath(playerTo, targets);
                            stopwatch.Stop();

                            foreach (var playerFrom in players)
                            {
                                if (!playerTo.Id.SteamId.Equals(playerFrom.Id.SteamId)) if (playerFrom.Character != null) SendQualityPacket(playerFrom.Id.SteamId, playerTo.Id.SteamId, (float)Math.Sqrt(connections.GetValueOrDefault(playerFrom.Character.EntityId, 0)));
                            }
                            foreach (var grid in debugGrids)
                            {
                                SendQualityPacket(unchecked((ulong)grid.EntityId), playerTo.Id.SteamId, (float)Math.Sqrt(connections.GetValueOrDefault(grid.EntityId, 0)));
                            }
                        }
                    }
                }
                else
                {
                    foreach (var playerTo in players)
                    {
                        if (playerTo.Character != null)
                        {
                            stopwatch.Start();
                            _playerConnections.Clear();
                            WalkNetwork(playerTo);
                            stopwatch.Stop();
                            foreach (var playerFrom in players)
                            {
                                if (!playerTo.Id.SteamId.Equals(playerFrom.Id.SteamId)) SendQualityPacket(playerFrom.Id.SteamId, playerTo.Id.SteamId, (float)Math.Sqrt(_playerConnections.GetValueOrDefault(playerFrom.Id.SteamId, 0)));
                            }
                            foreach (var grid in debugGrids)
                            {
                                SendQualityPacket(unchecked((ulong)grid.EntityId), playerTo.Id.SteamId, (float)Math.Sqrt(_playerConnections.GetValueOrDefault(unchecked((ulong)grid.EntityId), 0)));
                            }
                        }
                    }
                }
                performanceLogs[performanceIndex] = stopwatch.Elapsed.TotalMilliseconds;
                performanceIndex = (performanceIndex + 1) % performanceLogs.Length;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            if (wssv != null) wssv.Stop();
        }

        private void SetupConfig()
        {
            var configFile = Path.Combine(StoragePath, "../SignalServerConfig.cfg");
            try
            {
                _config = Persistent<SignalServerConfig>.Load(configFile);
            } catch (Exception e)
            {
                Log.Warn(e);
            }
            if (_config == null || _config.Data == null)
            {
                if (DebugWalk) Log.Info("Create Default Config, because none was found");

                _config = new Persistent<SignalServerConfig>(configFile, new SignalServerConfig());
                _config.Save();
            }
        }
    }
}
