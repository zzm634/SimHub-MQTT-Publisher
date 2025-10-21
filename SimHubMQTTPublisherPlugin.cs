using GameReaderCommon;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json;
using SimHub.MQTTPublisher.Settings;
using SimHub.Plugins;
using SimHub.Plugins.OutputPlugins.Dash.TemplatingCommon;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Markup;
using System.Windows.Media;

namespace SimHub.MQTTPublisher
{
    [PluginDescription("MQTT Publisher")]
    [PluginAuthor("Wotever")]
    [PluginName("MQTT Publisher")]
    public class SimHubMQTTPublisherPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public SimHubMQTTPublisherPluginSettings Settings;

        public SimHubMQTTPublisherPluginUserSettings UserSettings { get; private set; }

        private MqttFactory mqttFactory;
        private IMqttClient mqttClient;

        /// <summary>
        /// Instance of the current plugin manager
        /// </summary>
        public PluginManager PluginManager { get; set; }

        private NCalcEngineBase nCalcEngine = new NCalcEngineBase();

        /// <summary>
        /// Gets the left menu icon. Icon must be 24x24 and compatible with black and white display.
        /// </summary>
        public ImageSource PictureIcon => this.ToIcon(Properties.Resources.sdkmenuicon);

        /// <summary>
        /// Gets a short plugin title to show in left menu. Return null if you want to use the title as defined in PluginName attribute.
        /// </summary>
        public string LeftMenuTitle => "MQTT Publisher";

        private System.Timers.Timer publishTimer;
        private string lastPublishedValue = null;
        private bool publishImmediate = false;

        /// <summary>
        /// Called one time per game data update, contains all normalized game data,
        /// raw data are intentionnally "hidden" under a generic object type (A plugin SHOULD NOT USE IT)
        ///
        /// This method is on the critical path, it must execute as fast as possible and avoid throwing any error
        ///
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <param name="data">Current game data, including current and previous data frame.</param>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (publishImmediate)
            {
                PublishMQTTMessage();
            }
        }

        private void PublishMQTTMessage()
        {
            if (Settings.Expression != null)
            {

                var result = this.nCalcEngine.ParseValue(Settings.Expression);

                var outputString = result == null ? "" : result.ToString();

                if (!string.Equals(outputString, lastPublishedValue))
                {

                    var applicationMessageBuilder = new MqttApplicationMessageBuilder()
                    .WithTopic(Settings.Topic)
                    .WithRetainFlag(true);
                    if (result != null)
                    {
                        applicationMessageBuilder = applicationMessageBuilder.WithPayload(outputString);
                    }
                    var applicationMessage = applicationMessageBuilder.Build();

                    Task.Run(async () => await mqttClient.PublishAsync(applicationMessage, CancellationToken.None)).Wait();
                }
                lastPublishedValue = outputString;
            }
        }

        /// <summary>
        /// Called at plugin manager stop, close/dispose anything needed here !
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void End(PluginManager pluginManager)
        {
            // Save settings
            this.SaveCommonSettings("GeneralSettings", Settings);
            this.SaveCommonSettings("UserSettings", UserSettings);
            if (this.publishTimer != null)
            {
                this.publishTimer.Stop();
                this.publishTimer.Dispose();
            }
            this.publishImmediate = false;
            mqttClient.Dispose();
        }

        /// <summary>
        /// Returns the settings control, return null if no settings control is required
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <returns></returns>
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SimHubMQTTPublisherPluginUI(this);
        }

        /// <summary>
        /// Called once after plugins startup
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("Starting plugin");

            // Load settings
            Settings = this.ReadCommonSettings<SimHubMQTTPublisherPluginSettings>("GeneralSettings", () => new SimHubMQTTPublisherPluginSettings());

            UserSettings = this.ReadCommonSettings<SimHubMQTTPublisherPluginUserSettings>("UserSettings", () => new SimHubMQTTPublisherPluginUserSettings());

            this.mqttFactory = new MqttFactory();

            CreateMQTTClient();

            if (Settings.PublishInterval <= 0)
            {
                this.publishImmediate = true;
            }
            else
            {
                this.publishImmediate = false;
                this.publishTimer = new System.Timers.Timer(Settings.PublishInterval);
                this.publishTimer.Elapsed += (s, e) =>
                {
                    PublishMQTTMessage();
                };
                this.publishTimer.AutoReset = true;
                this.publishTimer.Enabled = true;
            }
        }

        internal void CreateMQTTClient()
        {
            var newmqttClient = mqttFactory.CreateMqttClient();

            var mqttClientOptions = new MqttClientOptionsBuilder()
               .WithTcpServer(Settings.Server)
               .WithCredentials(Settings.Login, Settings.Password)
               .Build();

            newmqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

            var oldMqttClient = this.mqttClient;

            mqttClient = newmqttClient;

            if (oldMqttClient != null)
            {
                oldMqttClient.Dispose();
            }
        }
    }
}