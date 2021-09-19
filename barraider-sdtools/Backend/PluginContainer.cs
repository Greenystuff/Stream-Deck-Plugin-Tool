﻿using Newtonsoft.Json.Linq;
using streamdeck_client_csharp;
using streamdeck_client_csharp.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BarRaider.SdTools
{
    class PluginContainer
    {
        private StreamDeckConnection connection;
        private readonly ManualResetEvent connectEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent disconnectEvent = new ManualResetEvent(false);
        private readonly SemaphoreSlim instancesLock = new SemaphoreSlim(1);
        private string pluginUUID = null;
        private StreamDeckInfo deviceInfo;

        private static readonly Dictionary<string, Type> supportedActions = new Dictionary<string, Type>();

        // Holds all instances of plugin
        private static readonly Dictionary<string, PluginBase> instances = new Dictionary<string, PluginBase>();

        public PluginContainer(PluginActionId[] supportedActionIds)
        {
            foreach (PluginActionId action in supportedActionIds)
            {
                supportedActions[action.ActionId] = action.PluginBaseType;
            }
        }

        public void Run(StreamDeckOptions options)
        {
            pluginUUID = options.PluginUUID;
            deviceInfo = options.DeviceInfo;
            connection = new StreamDeckConnection(options.Port, options.PluginUUID, options.RegisterEvent);

            // Register for events
            connection.OnConnected += Connection_OnConnected;
            connection.OnDisconnected += Connection_OnDisconnected;
            connection.OnKeyDown += Connection_OnKeyDown;
            connection.OnKeyUp += Connection_OnKeyUp;
            connection.OnWillAppear += Connection_OnWillAppear;
            connection.OnWillDisappear += Connection_OnWillDisappear;

            // Settings changed
            connection.OnDidReceiveSettings += Connection_OnDidReceiveSettings;
            connection.OnDidReceiveGlobalSettings += Connection_OnDidReceiveGlobalSettings;

            // Start the connection
            connection.Run();
#if DEBUG
            Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin Loaded: UUID: {pluginUUID} Device Info: {deviceInfo}");
#endif
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Plugin version: {deviceInfo.Plugin.Version}");
            Logger.Instance.LogMessage(TracingLevel.INFO, "Connecting to Stream Deck");

            // Wait for up to 10 seconds to connect
            if (connectEvent.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "Connected to Stream Deck");

                // Initialize GlobalSettings manager
                GlobalSettingsManager.Instance.Initialize(connection);

                // We connected, loop every second until we disconnect
                while (!disconnectEvent.WaitOne(TimeSpan.FromMilliseconds(1000)))
                {
                    RunTick();
                }
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, "Plugin Disconnected - Exiting");
        }

        // Button pressed
        private async void Connection_OnKeyDown(object sender, StreamDeckEventReceivedEventArgs<KeyDownEvent> e)
        {
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin Keydown: Context: {e.Event.Context} Action: {e.Event.Action} Payload: {e.Event.Payload?.ToStringEx()}");
#endif

                if (instances.ContainsKey(e.Event.Context))
                {
                    KeyPayload payload = new KeyPayload(GenerateKeyCoordinates(e.Event.Payload.Coordinates),
                                                        e.Event.Payload.Settings, e.Event.Payload.State, e.Event.Payload.UserDesiredState, e.Event.Payload.IsInMultiAction);
                    instances[e.Event.Context].KeyPressed(payload);
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }

        // Button released
        private async void Connection_OnKeyUp(object sender, StreamDeckEventReceivedEventArgs<KeyUpEvent> e)
        {
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin Keyup: Context: {e.Event.Context} Action: {e.Event.Action} Payload: {e.Event.Payload?.ToStringEx()}");
#endif

                if (instances.ContainsKey(e.Event.Context))
                {
                    KeyPayload payload = new KeyPayload(GenerateKeyCoordinates(e.Event.Payload.Coordinates),
                                                        e.Event.Payload.Settings, e.Event.Payload.State, e.Event.Payload.UserDesiredState, e.Event.Payload.IsInMultiAction);
                    instances[e.Event.Context].KeyReleased(payload);
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }


        // Function runs every second, used to update UI
        private async void RunTick()
        {
            await instancesLock.WaitAsync();
            try
            {
                foreach (KeyValuePair<string, PluginBase> kvp in instances.ToArray())
                {
                    kvp.Value.OnTick();
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }

        // Action is loaded in the Stream Deck
        private async void Connection_OnWillAppear(object sender, StreamDeckEventReceivedEventArgs<WillAppearEvent> e)
        {
            SDConnection conn = new SDConnection(connection, pluginUUID, deviceInfo, e.Event.Action, e.Event.Context, e.Event.Device);
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin OnWillAppear: Context: {e.Event.Context} Action: {e.Event.Action} Payload: {e.Event.Payload?.ToStringEx()}");
#endif

                if (supportedActions.ContainsKey(e.Event.Action))
                {
                    try
                    {
                        if (instances.ContainsKey(e.Event.Context) && instances[e.Event.Context] != null)
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, $"WillAppear called for already existing context {e.Event.Context} (might be inside a multi-action)");
                            return;
                        }
                        InitialPayload payload = new InitialPayload(GenerateKeyCoordinates(e.Event.Payload.Coordinates),
                                                                    e.Event.Payload.Settings, e.Event.Payload.State, e.Event.Payload.IsInMultiAction, deviceInfo);
                        instances[e.Event.Context] = (PluginBase)Activator.CreateInstance(supportedActions[e.Event.Action], conn, payload);
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.FATAL, $"Could not create instance of {supportedActions[e.Event.Action]} with context {e.Event.Context} - This may be due to an Exception raised in the constructor, or the class does not inherit PluginBase with the same constructor {ex}");
                    }
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"No plugin found that matches action: {e.Event.Action}");
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }

        private async void Connection_OnWillDisappear(object sender, StreamDeckEventReceivedEventArgs<WillDisappearEvent> e)
        {
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin OnWillDisappear: Context: {e.Event.Context} Action: {e.Event.Action} Payload: {e.Event.Payload?.ToStringEx()}");
#endif

                if (instances.ContainsKey(e.Event.Context))
                {
                   instances[e.Event.Context].Destroy();
                   instances.Remove(e.Event.Context);
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }

        // Settings updated
        private async void Connection_OnDidReceiveSettings(object sender, StreamDeckEventReceivedEventArgs<DidReceiveSettingsEvent> e)
        {
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin OnDidReceiveSettings: Context: {e.Event.Context} Action: {e.Event.Action} Payload: {e.Event.Payload?.ToStringEx()}");
#endif

                if (instances.ContainsKey(e.Event.Context))
                {
                    instances[e.Event.Context].ReceivedSettings(JObject.FromObject(e.Event.Payload).ToObject<ReceivedSettingsPayload>());
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }

        // Global settings updated
        private async void Connection_OnDidReceiveGlobalSettings(object sender, StreamDeckEventReceivedEventArgs<DidReceiveGlobalSettingsEvent> e)
        {
            await instancesLock.WaitAsync();
            try
            {
#if DEBUG
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Plugin OnDidReceiveGlobalSettings: Settings: {e.Event.Payload?.ToStringEx()}");
#endif

                var globalSettings = JObject.FromObject(e.Event.Payload).ToObject<ReceivedGlobalSettingsPayload>();
                foreach (string key in instances.Keys)
                {
                    instances[key].ReceivedGlobalSettings(globalSettings);
                }
            }
            finally
            {
                instancesLock.Release();
            }
        }


        private void Connection_OnConnected(object sender, EventArgs e)
        {
            connectEvent.Set();
        }

        private void Connection_OnDisconnected(object sender, EventArgs e)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Disconnect event received");
            disconnectEvent.Set();
        }

        private KeyCoordinates GenerateKeyCoordinates(Coordinates coordinates)
        {
            if (coordinates == null)
            {
                return null;
            }

            return new KeyCoordinates() { Column = coordinates.Columns, Row = coordinates.Rows };
        }

    }
}
