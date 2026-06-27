using System;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Insidash.TallyConnector
{
    public class ActivationWindow : Form
    {
        private TextBox _keyInput;
        private Button _connectBtn;
        private Label _statusLabel;
        private readonly string _targetTallyCompany;
        private static readonly HttpClient _client = new HttpClient();

        public ActivationWindow(string targetTallyCompany = null)
        {
            _targetTallyCompany = targetTallyCompany;

            if (!string.IsNullOrWhiteSpace(_targetTallyCompany))
            {
                Text = $"Link Tally Company: {_targetTallyCompany}";
            }
            else
            {
                Text = "Insidash Tally Connector — Activation";
            }

            Size = new System.Drawing.Size(420, 220);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var label = new Label
            {
                Text = string.IsNullOrWhiteSpace(_targetTallyCompany)
                    ? "Enter your Activation Key:"
                    : $"Enter Activation Key for '{_targetTallyCompany}':",
                Location = new System.Drawing.Point(20, 30),
                AutoSize = true
            };

            _keyInput = new TextBox
            {
                Location = new System.Drawing.Point(20, 55),
                Width = 360,
                CharacterCasing = CharacterCasing.Upper,
                Font = new System.Drawing.Font("Consolas", 12)
            };

            _connectBtn = new Button
            {
                Text = "Activate",
                Location = new System.Drawing.Point(20, 95),
                Width = 360,
                Height = 35
            };
            _connectBtn.Click += OnActivateClick;

            _statusLabel = new Label
            {
                Location = new System.Drawing.Point(20, 140),
                Width = 360,
                AutoSize = false,
                ForeColor = System.Drawing.Color.Red
            };

            Controls.AddRange(new Control[] { label, _keyInput, _connectBtn, _statusLabel });
        }

        private async void OnActivateClick(object sender, EventArgs e)
        {
            string key = _keyInput.Text.Trim();
            if (key.Length != 16)
            {
                _statusLabel.Text = "Key must be exactly 16 characters.";
                _statusLabel.ForeColor = System.Drawing.Color.Red;
                return;
            }

            _connectBtn.Enabled = false;
            _statusLabel.Text = "Activating...";
            _statusLabel.ForeColor = System.Drawing.Color.Gray;

            try
            {
                string apiBase = System.Configuration.ConfigurationManager.AppSettings["ApiBaseUrl"];

                // If the App.config key is missing, empty, or pointing to old test ports,
                // we override it and force it to connect to your active local server on port 2244
                if (string.IsNullOrWhiteSpace(apiBase)
                    || apiBase.Contains("localhost:8081")
                    || apiBase.Contains("127.0.0.1:8081")
                    || apiBase.Contains("your-server")
                    || apiBase.Contains("your-cloud-server"))
                {
                  
                }

                //apiBase = "http://localhost:2244";

                var body = JsonConvert.SerializeObject(new
                {
                    activationKey = key,
                    machineID = MachineIdentifier.Get(),
                    agentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                                        .GetName().Version.ToString(3)
                });

                using (var req = new HttpRequestMessage(HttpMethod.Post, apiBase.TrimEnd('/') + "/api/connector/activate"))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    var resp = await _client.SendAsync(req);

                    if (!resp.IsSuccessStatusCode)
                    {
                        _statusLabel.Text = "Invalid or already-used key. Contact Insidash support.";
                        _statusLabel.ForeColor = System.Drawing.Color.Red;
                        _connectBtn.Enabled = true;
                        return;
                    }

                    string json = await resp.Content.ReadAsStringAsync();
                    dynamic data = JsonConvert.DeserializeObject(json);

                    // ── UPDATE: Load existing config, modify active profile, and save safely ──
                    var config = LocalConfig.Exists() ? LocalConfig.Load() : new ConnectorConfig();

                    // Match profiles dictionary initialization
                    if (config.Profiles == null)
                    {
                        config.Profiles = new System.Collections.Generic.Dictionary<string, CompanyProfile>(StringComparer.OrdinalIgnoreCase);
                    }

                    string targetCompany = string.IsNullOrWhiteSpace(_targetTallyCompany)
                        ? (string.IsNullOrWhiteSpace(config.TallyCompanyName) ? "Default Company" : config.TallyCompanyName)
                        : _targetTallyCompany;

                    // Insert or overwrite the specific profile map
                    config.Profiles[targetCompany] = new CompanyProfile
                    {
                        SyncToken = (string)data.syncToken,
                        CompanyID = (int)data.companyId
                    };

                    config.TallyCompanyName = targetCompany;

                    // Update secondary configuration variables if provided
                    if (data.tallyHost != null) config.TallyHost = (string)data.tallyHost;
                    if (data.tallyPort != null) config.TallyPort = (string)data.tallyPort;
                    if (data.syncIntervalMs != null) config.SyncIntervalMs = (int)data.syncIntervalMs;

                    LocalConfig.Save(config);

                    MessageBox.Show(
                        $"✓ '{targetCompany}' successfully linked to Insidash dashboard!\n\n" +
                        "Tally data will sync automatically in the background.",
                        "Insidash Connected", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Connection error: {ex.Message}";
                _statusLabel.ForeColor = System.Drawing.Color.Red;
                _connectBtn.Enabled = true;
            }
        }
    }
}