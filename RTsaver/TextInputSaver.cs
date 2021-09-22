using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;
using AsyncWindowsClipboard;
using System.Drawing;
using System.IO;

namespace RtSaver
{
    [PluginActionId("com.greenstuff.rtsaver")]
    public class TextInputSaver : PluginBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.InputText = String.Empty;
                instance.Background = String.Empty;

                return instance;
            }

            [JsonProperty(PropertyName = "inputText")]
            public string InputText { get; set; }

            [JsonProperty(PropertyName = "selected_background")]
            public string Background { get; set; }
        }

        #region Private members

        private const int RESET_COUNTER_KEYPRESS_LENGTH = 1;

        private bool inputRunning = false;
        private PluginSettings settings;

        #endregion

        #region Public Methods

        public TextInputSaver(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                this.settings = payload.Settings.ToObject<PluginSettings>();
            }
        }

        private async void SetImage(string path)
        {

            var b64img = Convert.ToBase64String(File.ReadAllBytes(path), Base64FormattingOptions.None);
            await Connection.SetImageAsync(b64img);

        }

        public override void KeyPressed(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
            if (inputRunning)
            {
                return;
            }

            string text = settings.InputText;
            CopyTextToClipboard(text);
            string imagePath = "Images/Background_images/bg-administratif.png";
            SetImage(imagePath);

        }

        public override void KeyReleased(KeyPayload payload)
        {
            SendInput();

        }

        public override void OnTick()
        {
        }

        public async void CopyTextToClipboard(String textToCopy)
        {
            var clipboardService = new WindowsClipboardService();
            if (textToCopy != "")
            {
                await clipboardService.SetTextAsync(textToCopy); // Sets the text
            }
        }

        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Destructor called");
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            // New in StreamDeck-Tools v2.0:
            Tools.AutoPopulateSettings(settings, payload.Settings);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Settings loaded: {payload.Settings}");
            

        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
        { }

        #endregion

        #region Private Methods

        private async void SendInput()
        {
            inputRunning = true;
            await Task.Run(() =>
            {
              InputSimulator iis = new InputSimulator();
              iis.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.CONTROL, WindowsInput.Native.VirtualKeyCode.VK_V);
                
            });
            inputRunning = false;
        }
        #endregion
    }

}
