﻿using static Oxide.Plugins.Core;
using System.Net;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Teleport", "Game Limits", "2.0.0")]
    [Description("The teleport plugin")]

    public class Teleport : RustPlugin
    {
        private Dictionary<ulong, Dictionary<string, Vector3>> playerHomes = new Dictionary<ulong, Dictionary<string, Vector3>>();

        #region Oxide Hooks
        [ChatCommand("h")]
        private void OnChatCommandH(BasePlayer player, string command, string[] args)
        {
            OnChatCommandHome(player, command, args);
        }

        [ChatCommand("home")]
        private void OnChatCommandHome(BasePlayer player, string command, string[] args)
        {
            if (player == null || PlayerData.Get(player) == null)
                return;

            string syntax = "/home \"name\" <color=#999>Start the teleport to your home</color>\n" +
                "/home list <color=#999>Shows a list of your homes</color>\n" +
                "/home add \"name\" <color=#999>Add a home to your homelist</color>\n" +
                "/home remove \"name\" <color=#999>Removes a home from your homelist</color>";

            if (args.Length < 1)
            {
                CreateHomeUI(player);
                player.ChatMessage(syntax);
                return;
            }

            switch (args[0])
            {
                case "list":
                    if (playerHomes[player.userID].Count == 0)
                    {
                        player.ChatMessage("You dont have any homes, you can add them with /home add \"name\"");
                        return;
                    }

                    string homeList = "Your home(s):\n";
                    foreach (var home in playerHomes[player.userID])
                        homeList += $" - {home.Key}\n";

                    player.ChatMessage(homeList.TrimEnd('\n'));
                    return;

                case "add":
                    if (args.Length != 2)
                    {
                        player.ChatMessage(syntax);
                        return;
                    }

                    AddHome(player, args[1]);
                    return;

                case "remove":
                    if (args.Length != 2)
                    {
                        player.ChatMessage(syntax);
                        return;
                    }

                    DeleteHome(player, args[1]);
                    return;
            }

            if (args.Length != 1)
            {
                player.ChatMessage(syntax);
                return;
            }

            TeleportHome(player, args[0]);
        }

        [ConsoleCommand("teleport")]
        private void OnConsoleCommandTeleport(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.Connection.player == null || !arg.HasArgs())
                return;

            BasePlayer player = (BasePlayer)arg.Connection.player;

            switch (arg.GetString(0))
            {
                case "home":
                    switch (arg.GetString(1))
                    {
                        case "close":
                            DestroyHomeUI(player);
                            break;

                        case "add":
                            AddHome(player, arg.GetString(2, "1"));
                            break;

                        case "remove":
                            DeleteHome(player, arg.GetString(2, "1"));
                            break;

                        case "teleport":
                            TeleportHome(player, arg.GetString(2, "1"));
                            DestroyHomeUI(player);
                            break;

                        case "index":
                            UpdateHomeUI(player, arg.GetInt(2, 0));
                            break;
                    }
                    break;
            }
        }

        private void Init()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                DestroyHomeUI(player);
                LoadPlayerHomes(player);
            }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            LoadPlayerHomes(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            UnloadPlayerHomes(player);
        }
        #endregion

        #region Functions
        private void LoadPlayerHomes(BasePlayer player)
        {
            if (player == null)
                return;

            PlayerData.PData pdata = PlayerData.Get(player);

            if (pdata == null)
            {
                Puts($"Loading homes for [{player.UserIDString}] has been delayed, waiting for the PlayerData plugin");
                timer.Once(1f, () => LoadPlayerHomes(player));
                return;
            }

            // Remove the old homes first
            UnloadPlayerHomes(player);


            Database.Query(Database.Build("SELECT * FROM homes WHERE user_id = @0;", pdata.id), records =>
            {
                Dictionary<string, Vector3> homes = new Dictionary<string, Vector3>();

                foreach (var record in records)
                    homes.Add(Convert.ToString(record["name"]), new Vector3(
                        Convert.ToSingle(record["x"]),
                        Convert.ToSingle(record["y"]),
                        Convert.ToSingle(record["z"])));

                playerHomes.Add(player.userID, homes);

                Puts($"Loaded {records.Count} home(s) for [{pdata.id}:{player.UserIDString}]");
            });
        }

        private void UnloadPlayerHomes(BasePlayer player)
        {
            if (playerHomes.ContainsKey(player.userID))
                playerHomes.Remove(player.userID);
        }

        private string CanTeleportFrom(BasePlayer player)
        {
            if (!player.IsAlive())
                return "dead";
            if (player.IsWounded())
                return "wounded";
            if (!player.CanBuild())
                return "building blocked";
            if (player.IsSwimming())
                return "swimming";
            if (player.inventory.crafting.queue.Count > 0)
                return "crafting";
            return null;
        }

        private string CanTeleportToPosition(BasePlayer player, Vector3 position)
        {
            return null;
        }

        private string CanTeleportToPlayer(BasePlayer player, BasePlayer target)
        {
            return null;
        }

        private void AddHome(BasePlayer player, string name)
        {
            PlayerData.PData pdata = PlayerData.Get(player);

            if (player == null || pdata == null)
                return;

            if (playerHomes[player.userID].ContainsKey(name))
            {
                player.ChatMessage($"<color=#d00>Error</color> the home with the name \"{name}\" already exists.");
                return;
            }

            string teleportFrom = CanTeleportFrom(player);
            if (teleportFrom != null)
            {
                player.ChatMessage($"<color=#d00>Error</color> cannot create homepoint ({teleportFrom}).");
                return;
            }

            if (!Helper.HasMinimumVipRank(pdata, "vip") && playerHomes[player.userID].Count >= 1)
            {
                player.ChatMessage($"<color=#d00>Error</color> unable to set your home here, you have reached the maximum of 1 home!");
                return;
            }
            else if (Helper.HasMinimumVipRank(pdata, "vip") && playerHomes[player.userID].Count >= 3)
            {
                player.ChatMessage($"<color=#d00>Error</color> unable to set your home here, you have reached the maximum of 3 homes!");
                return;
            }

            playerHomes[player.userID].Add(name, player.transform.position);

            Database.Insert(Database.Build("INSERT INTO homes (user_id, name, x, y, z) VALUES (@0, @1, @2, @3, @4);",
                pdata.id,
                name,
                player.transform.position.x,
                player.transform.position.y,
                player.transform.position.z));

            player.ChatMessage($"Your home \"{name}\" has been added.");
        }

        private void DeleteHome(BasePlayer player, string name)
        {
            PlayerData.PData pdata = PlayerData.Get(player);

            if (player == null || pdata == null)
                return;

            if (!playerHomes[player.userID].ContainsKey(name))
            {
                player.ChatMessage($"<color=#d00>Error</color> the home with the name \"{name}\" doest not exists.");
                return;
            }

            playerHomes[player.userID].Remove(name);

            Database.Delete(Database.Build("DELETE FROM homes WHERE user_id=@0 AND name=@1 LIMIT 1;", pdata.id, name));

            player.ChatMessage($"Your home \"{name}\" has been removed.");
        }

        private void TeleportHome(BasePlayer player, string name)
        {
            PlayerData.PData pdata = PlayerData.Get(player);

            if (player == null || pdata == null)
                return;

            // Check if home exists
            if (!playerHomes[player.userID].ContainsKey(name))
            {
                player.ChatMessage($"<color=#d00>Error</color> the home with the name \"{name}\" does not exists.");
                return;
            }

            // Check for the cooldown
            int cooldown = pdata.HasCooldown("teleport_home");
            if (cooldown > 0)
            {
                player.ChatMessage($"<color=#d00>Error</color> teleport cooldown {Helper.TimeFormat.Long(cooldown)}.");
                return;
            }

            TeleportHomeStart(player, playerHomes[player.userID][name], 10);
        }

        private void TeleportHomeStart(BasePlayer player, Vector3 position, int countdown = 0)
        {
            PlayerData.PData pdata = PlayerData.Get(player);

            if (player == null || position == null || pdata == null)
                return;

            // Check if the teleport from location is valid
            string teleportFrom = CanTeleportFrom(player);
            if (teleportFrom != null)
            {
                player.ChatMessage($"<color=#d00>Error</color> you cannot teleport from your current location ({teleportFrom}).");
                return;
            }

            // Check if the teleport destination is valid
            string teleportTo = CanTeleportToPosition(player, position);
            if (teleportTo != null)
            {
                player.ChatMessage($"<color=#d00>Error</color> you cannot teleport to your home ({teleportTo}).");
                return;
            }

            // When there is a countdown timer, intialize a timer and notification timer
            if (countdown > 0)
            {
                Notifications.AddTimedNotification(player, "teleport_home", "Teleport Home", countdown, "0.3 0.3 0.3 1");
                timer.Once(countdown, () => TeleportHomeStart(player, position));
                return;
            }

            // Remove the notification timer
            Notifications.RemoveTimedNotification(player, "teleport_home");

            // Set the cooldown for the teleport
            int cooldownDuration = 60 * 20;
            if (Helper.HasMinimumVipRank(pdata, "vip"))
                cooldownDuration = 60 * 5;

            // Set the cooldown timer
            pdata.AddCooldown("teleport_home", cooldownDuration);

            // Execute the teleportation
            ExecuteTeleport(player, position);
        }

        private void ExecuteTeleport(BasePlayer player, Vector3 target)
        {
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "StartLoading");

            if (!player.IsSleeping())
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
                if (!BasePlayer.sleepingPlayerList.Contains(player))
                    BasePlayer.sleepingPlayerList.Add(player);
                player.CancelInvoke("InventoryUpdate");
            }

            player.MovePosition(target);
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "ForcePositionTo", target);
            if (player.net?.connection != null)
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            player.UpdateNetworkGroup();
            //player.UpdatePlayerCollider(true);
            player.SendNetworkUpdateImmediate(false);
            if (player.net?.connection == null)
                return;

            try
            {
                player.ClearEntityQueue(null);
            }
            catch { }
            player.SendFullSnapshot();
        }
        #endregion

        #region UI
        private void CreateHomeUI(BasePlayer player)
        {
            if (player == null)
                return;

            DestroyHomeUI(player);

            CuiElementContainer container = Helper.UI.Container("ui_teleport_home", "0 0 0 .99", "0.35 0.05", "0.65 0.95", true);

            Helper.UI.Add(player, container);

            UpdateHomeUI(player);
        }

        private void UpdateHomeUI(BasePlayer player, int index = 0)
        {
            Helper.UI.Destroy(player, "ui_teleport_home_toolbar");
            Helper.UI.Destroy(player, "ui_teleport_home_entries");

            // Create the home items themself
            CuiElementContainer entriesContainer = Helper.UI.Container("ui_teleport_home_entries", "0 0 0 0", "0 0.04", "0.997 1", false, "ui_teleport_home");
            int i = 0;
            foreach (var home in playerHomes[player.userID].Skip(index * 15).Take(15))
            {
                Vector2 dimension = new Vector2(0.997f, 0.05f);
                Vector2 origin = new Vector2(0.0f, 1f);
                Vector2 offset = new Vector2(0f, (0.013f + dimension.y) * (i + 1));
                Vector2 min = origin - offset;
                Vector2 max = min + dimension;

                Helper.UI.Panel(ref entriesContainer, "ui_teleport_home_entries", "1 1 1 0.02", $"{min.x} {min.y}", $"{max.x} {max.y}");
                Helper.UI.Label(ref entriesContainer, "ui_teleport_home_entries", "1 1 1 1", $"Home {home.Key}", 12, $"{min.x + 0.02} {min.y}", $"{max.x} {max.y}", TextAnchor.MiddleLeft);
                Helper.UI.Button(ref entriesContainer, "ui_teleport_home_entries", "0.8 0.2 0.2 1", "Delete", 12, $"{min.x + 0.8} {min.y + 0.01}", $"{max.x - 0.02} {max.y - 0.01}", $"teleport home remove {home.Key}");
                Helper.UI.Button(ref entriesContainer, "ui_teleport_home_entries", "0.12 0.38 0.57 1", "Teleport", 12, $"{min.x + 0.598} {min.y + 0.01}", $"{max.x - 0.22} {max.y - 0.01}", $"teleport home teleport {home.Key}");

                i++;
            }

            // Create toolbar container
            CuiElementContainer toolbarContainer = Helper.UI.Container("ui_teleport_home_toolbar", "0 0 0 0", "0 0", "0.997 0.04", false, "ui_teleport_home");

            if (index > 0)
                Helper.UI.Button(ref toolbarContainer, "ui_teleport_home_toolbar", "0.12 0.38 0.57 1", "< Previous", 12, "0 0", "0.2 0.96", $"teleport home index {index - 1}");

            if (playerHomes[player.userID].Count - (index * 15) > 15)
                Helper.UI.Button(ref toolbarContainer, "ui_teleport_home_toolbar", "0.12 0.38 0.57 1", "Next >", 12, "0.8 0", "0.997 0.96", $"teleport home index {index + 1}");

            Helper.UI.Button(ref toolbarContainer, "ui_teleport_home_toolbar", "0.12 0.38 0.57 1", "Add Home", 12, $"{(index > 0 ? "0.21" : "0")} 0", "0.495 0.96", $"teleport home add {index}");
            Helper.UI.Button(ref toolbarContainer, "ui_teleport_home_toolbar", "0.8 0.2 0.2 1", "Close Homes", 12, "0.505 0", $"{(playerHomes[player.userID].Count - (index * 15) > 15 ? "0.79" : "0.997")} 0.96", $"teleport home close");

            Helper.UI.Add(player, entriesContainer);
            Helper.UI.Add(player, toolbarContainer);
        }

        private void DestroyHomeUI(BasePlayer player)
        {
            Helper.UI.Destroy(player, "ui_teleport_home");
        }
        #endregion
    }
}

