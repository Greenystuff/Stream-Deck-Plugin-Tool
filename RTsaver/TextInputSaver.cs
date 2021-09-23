using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using WindowsInput;
using AsyncWindowsClipboard;
using System.Drawing;

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
                instance.Background = "default";

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

        public async void SetImage(string path)
        {
            Image img = Image.FromFile(path);
            await Connection.SetImageAsync(img);
        }

        public override void KeyPressed(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
            if (inputRunning)
            {
                return;
            }
            
            if (settings.InputText != "")
            {
                string text = settings.InputText;
                CopyTextToClipboard(text);
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (settings.InputText != "") {
                SendInput();
            }
        }

        public override void OnTick()
        {
        }

        public async void CopyTextToClipboard(String textToCopy)
        {
            var clipboardService = new WindowsClipboardService();
            if (textToCopy != "")
            {
                await clipboardService.SetTextAsync(textToCopy);
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
            if (settings.Background != "default")
            {
                string imagePath = "Images/Background_images/" + settings.Background + ".png";
                SetImage(imagePath);
            }
            else
            {
                string imagePath = "Images/bg.png";
                SetImage(imagePath);
            }
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
            await Connection.ShowOk();
        }
        #endregion
    }
}
