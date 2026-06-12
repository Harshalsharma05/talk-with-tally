using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Insidash.TallyConnector
{
    public class CompanySelectWindow : Form
    {
        private ComboBox _companyDropdown;
        private Button   _saveBtn;
        private Label    _statusLabel;
        private ConnectorConfig _config;

        public CompanySelectWindow(ConnectorConfig config, List<string> companyNames)
        {
            _config = config;

            Text            = "Select Tally Company";
            Size            = new System.Drawing.Size(380, 200);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;

            var label = new Label {
                Text = "Which Tally company should sync to Insidash?",
                Location = new System.Drawing.Point(20, 20), AutoSize = true
            };

            _companyDropdown = new ComboBox {
                Location = new System.Drawing.Point(20, 50), Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            if (companyNames.Count == 0)
            {
                _companyDropdown.Items.Add("(No companies found — is Tally running?)");
                _companyDropdown.Enabled = false;
            }
            else
            {
                foreach (var name in companyNames)
                    _companyDropdown.Items.Add(name);

                // Pre-select currently configured company if it's in the list
                int idx = companyNames.IndexOf(config.TallyCompanyName);
                _companyDropdown.SelectedIndex = idx >= 0 ? idx : 0;
            }

            var hint = new Label {
                Text = "Insidash will only sync data for the selected company,\n" +
                       "even if a different company is focused in Tally's window.",
                Location = new System.Drawing.Point(20, 85), AutoSize = true,
                ForeColor = System.Drawing.Color.Gray, Font = new System.Drawing.Font("Segoe UI", 8)
            };

            _saveBtn = new Button {
                Text = "Save", Location = new System.Drawing.Point(270, 130),
                Width = 70, Enabled = companyNames.Count > 0
            };
            _saveBtn.Click += OnSave;

            _statusLabel = new Label {
                Location = new System.Drawing.Point(20, 135), Width = 230,
                AutoSize = false, ForeColor = System.Drawing.Color.Green
            };

            Controls.AddRange(new Control[] { label, _companyDropdown, hint, _saveBtn, _statusLabel });
        }

        private void OnSave(object sender, EventArgs e)
        {
            _config.TallyCompanyName = _companyDropdown.SelectedItem.ToString();
            LocalConfig.Save(_config);

            _statusLabel.Text = "✓ Saved. Will apply on next sync.";
            DialogResult = DialogResult.OK;

            // Close after a brief pause so user sees confirmation
            var timer = new System.Windows.Forms.Timer { Interval = 800 };
            timer.Tick += (s, ev) => { timer.Stop(); Close(); };
            timer.Start();
        }
    }
}