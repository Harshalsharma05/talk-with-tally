using System;
using System.Windows.Forms;

namespace Insidash.TallyConnector
{
    public class SettingsWindow : Form
    {
        private TextBox _hostInput, _portInput;
        private Button  _saveBtn;

        public SettingsWindow(ConnectorConfig config)
        {
            Text            = "Insidash Tally Connector — Settings";
            Size            = new System.Drawing.Size(380, 200);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;

            var hostLabel = new Label { Text = "Tally Host/IP:", Location = new System.Drawing.Point(20,30), AutoSize=true };
            _hostInput    = new TextBox { Text = config.TallyHost, Location = new System.Drawing.Point(130,27), Width=210 };

            var portLabel = new Label { Text = "Tally Port:", Location = new System.Drawing.Point(20,65), AutoSize=true };
            _portInput    = new TextBox { Text = config.TallyPort, Location = new System.Drawing.Point(130,62), Width=80 };

            var hint = new Label {
                Text = "Default: localhost / 9000\nChange only if Tally runs on a different machine.",
                Location = new System.Drawing.Point(20, 95), AutoSize = true,
                ForeColor = System.Drawing.Color.Gray
            };

            _saveBtn = new Button { Text = "Save", Location = new System.Drawing.Point(270,130), Width=70 };
            _saveBtn.Click += (s, e) =>
            {
                config.TallyHost = _hostInput.Text.Trim();
                config.TallyPort = _portInput.Text.Trim();
                LocalConfig.Save(config);
                MessageBox.Show("Settings saved. Next sync will use the new address.", "Saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            };

            Controls.AddRange(new Control[] { hostLabel, _hostInput, portLabel, _portInput, hint, _saveBtn });
        }
    }
}
