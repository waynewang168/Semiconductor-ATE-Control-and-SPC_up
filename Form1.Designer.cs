using System.Drawing;
using System.Windows.Forms;

namespace AteTestSystem_4
{
    partial class Form1
    {
        // UI 元件宣告
        private TextBox txtDutId;
        private Button btnStartSingle;
        private Button btnStartBatch;
        private Button btnEmergencyStop;
        private Label lblStatus;
        private RichTextBox rtbLog;
        private ProgressBar progressBar;

        private void InitializeCustomComponents()
        {
            this.Text = "ATE 自動化測試機台控制系統 v4.0 - [具備 Binning 分選與安全急停機制]";
            this.Size = new Size(720, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // 1. DUT 條碼區
            Label lblId = new Label()
            {
                Text = "晶片條碼 / DUT ID:",
                Location = new Point(25, 25),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            txtDutId = new TextBox()
            {
                Location = new Point(175, 23),
                Width = 160,
                Text = "DUT_SN_BATCH_001",
                Font = new Font("Segoe UI", 10)
            };

            // 2. 單顆測試按鈕
            btnStartSingle = new Button()
            {
                Text = "單顆測試 (Single)",
                Location = new Point(350, 20),
                Size = new Size(110, 32),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnStartSingle.Click += async (s, e) => await RunSingleTestAsync();

            // 3. 批次測試按鈕 (50 顆)
            btnStartBatch = new Button()
            {
                Text = "⚡ 批次自動 (50顆)",
                Location = new Point(470, 20),
                Size = new Size(115, 32),
                BackColor = Color.ForestGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnStartBatch.Click += async (s, e) => await RunBatchTestAsync(50);

            // 4. 緊急停止按鈕 (EMO)
            btnEmergencyStop = new Button()
            {
                Text = "⏹ 緊急停止 (STOP)",
                Location = new Point(595, 20),
                Size = new Size(100, 32),
                BackColor = Color.Crimson,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Enabled = false
            };
            btnEmergencyStop.Click += BtnEmergencyStop_Click;

            // 5. 測試進度條
            progressBar = new ProgressBar()
            {
                Location = new Point(25, 65),
                Size = new Size(670, 18),
                Visible = false
            };

            // 6. 機台看板狀態指示燈
            lblStatus = new Label()
            {
                Text = "機台就緒 (STANDBY)",
                Location = new Point(25, 95),
                Size = new Size(670, 45),
                BackColor = Color.LightGray,
                ForeColor = Color.Black,
                Font = new Font("Arial", 15, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 7. 即時日誌檢視區
            rtbLog = new RichTextBox()
            {
                Location = new Point(25, 150),
                Size = new Size(670, 390),
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 10)
            };

            // 掛載元件
            this.Controls.Add(lblId);
            this.Controls.Add(txtDutId);
            this.Controls.Add(btnStartSingle);
            this.Controls.Add(btnStartBatch);
            this.Controls.Add(btnEmergencyStop);
            this.Controls.Add(progressBar);
            this.Controls.Add(lblStatus);
            this.Controls.Add(rtbLog);
        }
    }
}