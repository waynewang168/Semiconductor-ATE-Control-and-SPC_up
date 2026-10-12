using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AteTestSystem_4
{
    // ==========================================
    // 半導體測試標準 Binning 枚舉 (擴充 Open/Short)
    // ==========================================
    public enum TestBin
    {
        Bin1_Pass = 1,        // 全項通過 (PASS)
        Bin2_LeakageFail = 2, // 待機電流異常 (Idd 超標)
        Bin3_VoltFail = 3,    // 供電電壓異常 (Vbus 塌陷)
        Bin4_TotalFail = 4,   // 多重直流參數缺陷 (Total Fail)(Idd 超標&&Vbus 塌陷)
        Bin5_OsFail = 5       // 連續性測試異常 (Open / Short 接觸缺陷)
    }

    public partial class Form1 : Form
    {
        private CancellationTokenSource _cts;
        private readonly Random _rand = new Random();
        private const string LogFolder = "TestLogs";

        public Form1()
        {
            InitializeCustomComponents();
            Directory.CreateDirectory(LogFolder);
        }

        private async Task RunSingleTestAsync()
        {
            string dutId = txtDutId.Text.Trim();
            if (string.IsNullOrEmpty(dutId))
            {
                MessageBox.Show("請輸入 DUT ID！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetUiState(isTesting: true);
            await ExecuteTestCycle(dutId, delayMs: 150);
            SetUiState(isTesting: false);
        }

        private async Task RunBatchTestAsync(int count)
        {
            _cts = new CancellationTokenSource();

            SetUiState(isTesting: true);
            progressBar.Visible = true;
            progressBar.Minimum = 0;
            progressBar.Maximum = count;
            progressBar.Value = 0;

            AppendLog($"\n>>> 開始執行批次自動化測試，排程顆數: {count} 顆 <<<");
            AppendLog(">>> 具備 Open/Short 治具保護、直流參數量測與 EMO 受控監控 <<<\n");

            bool isInterrupted = false;

            for (int i = 1; i <= count; i++)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    isInterrupted = true;
                    break;
                }

                string batchDutId = $"DUT_SN_{i:D3}";
                txtDutId.Text = batchDutId;

                await ExecuteTestCycle(batchDutId, delayMs: 40);

                progressBar.Value = i;
            }

            progressBar.Visible = false;
            SetUiState(isTesting: false);

            if (isInterrupted)
            {
                lblStatus.Text = "機台緊急中止 (EMO ACTIVATED)";
                lblStatus.BackColor = Color.DarkOrange;
                lblStatus.ForeColor = Color.Black;
                AppendLog("\n[警告] 批次測試已被作業員手動強制終止！已測試數據完整封存。\n");
                MessageBox.Show("機台已依指令安全煞停！", "安全中斷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                lblStatus.Text = "批次測試全數完成！";
                lblStatus.BackColor = Color.LightSkyBlue;
                lblStatus.ForeColor = Color.DarkBlue;
                MessageBox.Show($"批次測試已全數完成！\nLog 已存入 {LogFolder} 資料夾。", "批次完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            _cts.Dispose();
            _cts = null;
        }

        private void BtnEmergencyStop_Click(object sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                AppendLog(">>> [EMO] 作業員觸發緊急停止按鈕！發出中斷訊號... <<<");
                _cts.Cancel();
                btnEmergencyStop.Enabled = false;
            }
        }

        private async Task ExecuteTestCycle(string dutId, int delayMs)
        {
            AppendLog($"\n========== [START TEST] DUT: {dutId} ==========");
            lblStatus.Text = $"測試中... [{dutId}]";
            lblStatus.BackColor = Color.Khaki;
            lblStatus.ForeColor = Color.Black;

            // ----------------------------------------------------
            // 測項 0: Open / Short 連續性保護測試 (Force -100uA)
            // ----------------------------------------------------
            await Task.Delay(delayMs / 2);
            bool injectOsFail = _rand.Next(1, 100) <= 2; // 2% 治具接觸不良或短路
            double osVolt;
            if (injectOsFail)
            {
                // 模擬 Open (-1.5V 頂到 Clamp) 或 Short (0.0V 接地短路)
                osVolt = _rand.Next(0, 2) == 0 ? -1.50 : 0.00;
            }
            else
            {
                // 正常二極體順向壓降 (-0.65V ~ -0.75V)
                osVolt = -0.70 + (_rand.NextDouble() * 0.10 - 0.05);
            }
            bool passOs = (osVolt >= -0.90 && osVolt <= -0.20);
            AppendLog($"[0/3] Open/Short 測試: {osVolt:F2} V (Limit: -0.90 ~ -0.20) -> {(passOs ? "PASS" : "FAIL")}");

            // 若 Open/Short 失敗，直接分選為 Bin 5，並進行保護跳出（不執行後續大供電測試）
            if (!passOs)
            {
                TestBin binOs = TestBin.Bin5_OsFail;
                lblStatus.Text = $"{dutId} : FAIL [BIN 5]";
                lblStatus.BackColor = Color.Crimson;
                lblStatus.ForeColor = Color.White;
                AppendLog($"[防護阻斷] Open/Short 異常，終止供電以保護機台與治具！");
                AppendLog($"========== [TEST RESULT: FAIL | HARD_BIN: 5 (Bin5_OsFail)] ==========");
                SaveLogToFile(dutId, osVolt, passOs, 0.0, false, 0.0, false, "FAIL", binOs);
                return;
            }

            // ----------------------------------------------------
            // 測項 1: 待機靜態電流量測 (Idd)
            // ----------------------------------------------------
            await Task.Delay(delayMs);
            bool injectIdleFail = _rand.Next(1, 100) <= 2;
            double idleCurrent = injectIdleFail ? (55.0 + _rand.NextDouble() * 10) : (20.0 + _rand.NextDouble() * 20.0);
            bool passIdle = (idleCurrent >= 10.0 && idleCurrent <= 45.0);
            AppendLog($"[1/3] 靜態電流量測: {idleCurrent:F2} mA (Limit: 10.00 ~ 45.00) -> {(passIdle ? "PASS" : "FAIL")}");

            // ----------------------------------------------------
            // 測項 2: 供電引腳電壓量測 (Vbus)
            // ----------------------------------------------------
            await Task.Delay(delayMs);
            bool injectVoltFail = _rand.Next(1, 100) <= 2;
            double vbusVolt = injectVoltFail ? (4.65 + _rand.NextDouble() * 0.05) : (4.90 + _rand.NextDouble() * 0.20);
            bool passVolt = (vbusVolt >= 4.75 && vbusVolt <= 5.25);
            AppendLog($"[2/3] 供電電壓量測: {vbusVolt:F3} V (Limit: 4.750 ~ 5.250) -> {(passVolt ? "PASS" : "FAIL")}");

            // ----------------------------------------------------
            // 判定與分選 (Binning)
            // ----------------------------------------------------
            TestBin binResult = TestBin.Bin1_Pass;
            if (!passIdle && !passVolt) binResult = TestBin.Bin4_TotalFail;
            else if (!passIdle) binResult = TestBin.Bin2_LeakageFail;
            else if (!passVolt) binResult = TestBin.Bin3_VoltFail;

            bool finalPass = (binResult == TestBin.Bin1_Pass);
            string finalResult = finalPass ? "PASS" : "FAIL";

            lblStatus.Text = $"{dutId} : {finalResult} [BIN {(int)binResult}]";
            lblStatus.BackColor = finalPass ? Color.ForestGreen : Color.Crimson;
            lblStatus.ForeColor = Color.White;

            AppendLog($"========== [TEST RESULT: {finalResult} | HARD_BIN: {(int)binResult} ({binResult})] ==========");
            SaveLogToFile(dutId, osVolt, passOs, idleCurrent, passIdle, vbusVolt, passVolt, finalResult, binResult);
        }

        private void SaveLogToFile(string dutId, double osVolt, bool passOs, double idleCurrent, bool passIdle, double vbusVolt, bool passVolt, string finalResult, TestBin binResult)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string fileName = Path.Combine(LogFolder, $"{timestamp}_{dutId}.log");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           ATE AUTOMATED TEST LOG");
            sb.AppendLine("==================================================");
            sb.AppendLine($"TIMESTAMP      : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"DUT_ID         : {dutId}");
            sb.AppendLine($"OPERATOR_MODE  : AUTO_PIPELINE");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"ITEM_00_OPEN_SHORT   | VAL: {osVolt:F2} V   | LSL: -0.90 | USL: -0.20 | {(passOs ? "PASS" : "FAIL")}");
            sb.AppendLine($"ITEM_01_IDLE_CURRENT | VAL: {idleCurrent:F2} mA | LSL: 10.00 | USL: 45.00 | {(passIdle ? "PASS" : "FAIL")}");
            sb.AppendLine($"ITEM_02_VBUS_VOLT    | VAL: {vbusVolt:F3} V  | LSL: 4.750 | USL: 5.250 | {(passVolt ? "PASS" : "FAIL")}");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"OVERALL_RESULT : {finalResult}");
            sb.AppendLine($"HARD_BIN       : {(int)binResult} ({binResult})");
            sb.AppendLine("==================================================");

            File.WriteAllText(fileName, sb.ToString());
        }

        private void SetUiState(bool isTesting)
        {
            btnStartSingle.Enabled = !isTesting;
            btnStartBatch.Enabled = !isTesting;
            txtDutId.Enabled = !isTesting;
            btnEmergencyStop.Enabled = isTesting;
        }

        private void AppendLog(string message)
        {
            rtbLog.AppendText(message + Environment.NewLine);
            rtbLog.ScrollToCaret();
        }
    }
}