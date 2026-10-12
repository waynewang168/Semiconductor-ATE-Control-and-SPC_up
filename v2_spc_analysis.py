import os
import glob
import re
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
from scipy import stats

# 1. 設定繪圖風格與字體支援
plt.style.use('seaborn-v0_8-whitegrid' if 'seaborn-v0_8-whitegrid' in plt.style.available else 'default')
plt.rcParams['font.sans-serif'] = ['Microsoft JhengHei', 'Arial', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False

LOG_FOLDER = "."

def parse_log_files(folder_path):
    """
    解析 TestLogs 資料夾中所有結構化 ATE 測試日誌
    """
    log_files = glob.glob(os.path.join(folder_path, "*.log"))
    if not log_files:
        print(f"[警告] 找不到日誌檔案！請確認 '{folder_path}' 目錄下是否有 .log 檔案。")
        return pd.DataFrame()

    records = []
    
    re_dut = re.compile(r"DUT_ID\s*:\s*(\S+)")
    re_os = re.compile(r"ITEM_00_OPEN_SHORT\s*\|\s*VAL:\s*([-\d\.]+)\s*V")
    re_curr = re.compile(r"ITEM_01_IDLE_CURRENT\s*\|\s*VAL:\s*([\d\.]+)\s*mA")
    re_volt = re.compile(r"ITEM_02_VBUS_VOLT\s*\|\s*VAL:\s*([\d\.]+)\s*V")
    re_res = re.compile(r"OVERALL_RESULT\s*:\s*(\S+)")
    re_bin = re.compile(r"HARD_BIN\s*:\s*(\d+)\s*\(([^)]+)\)")

    for fpath in log_files:
        with open(fpath, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
            
            dut_match = re_dut.search(content)
            os_match = re_os.search(content)
            curr_match = re_curr.search(content)
            volt_match = re_volt.search(content)
            res_match = re_res.search(content)
            bin_match = re_bin.search(content)

            if res_match:
                curr_val = float(curr_match.group(1)) if curr_match else np.nan
                volt_val = float(volt_match.group(1)) if volt_match else np.nan
                
                # 若被 OS 阻斷供電 (0.0V / 0.0mA)，設為 NaN 避免扭曲 SPC
                if volt_val == 0.0: volt_val = np.nan
                if curr_val == 0.0: curr_val = np.nan

                records.append({
                    "DUT_ID": dut_match.group(1) if dut_match else "UNKNOWN",
                    "OS_Volt_V": float(os_match.group(1)) if os_match else -0.7,
                    "Idle_Current_mA": curr_val,
                    "Vbus_Volt_V": volt_val,
                    "Result": res_match.group(1),
                    "Bin_Code": int(bin_match.group(1)) if bin_match else 1,
                    "Bin_Name": bin_match.group(2) if bin_match else "Bin1_Pass"
                })

    df = pd.DataFrame(records)
    print(f"[資訊] 成功載入 {len(df)} 筆 DUT 測試數據。")
    return df

def calculate_spc_metrics(data, lsl, usl):
    """
    計算製程能力指標: Cp, Cpk, Mean, Std
    """
    valid_data = data.dropna()
    if len(valid_data) < 2:
        return {"Cp": 0, "Cpk": 0, "Mean": 0, "Std": 0, "LSL": lsl, "USL": usl}

    mean = np.mean(valid_data)
    std = np.std(valid_data, ddof=1)
    
    if std == 0:
        return {"Cp": 0, "Cpk": 0, "Mean": mean, "Std": 0, "LSL": lsl, "USL": usl}

    cp = (usl - lsl) / (6 * std)
    cpu = (usl - mean) / (3 * std)
    cpl = (mean - lsl) / (3 * std)
    cpk = min(cpu, cpl)

    return {
        "Cp": cp,
        "Cpk": cpk,
        "Mean": mean,
        "Std": std,
        "LSL": lsl,
        "USL": usl
    }

def generate_spc_dashboard(df):
    """
    繪製三合一工業級 SPC 品質與良率監控看板
    """
    if df.empty:
        return

    curr_lsl, curr_usl = 10.0, 45.0
    volt_lsl, volt_usl = 4.75, 5.25

    curr_data = df["Idle_Current_mA"].dropna()
    volt_data = df["Vbus_Volt_V"].dropna()

    curr_stats = calculate_spc_metrics(curr_data, curr_lsl, curr_usl)
    volt_stats = calculate_spc_metrics(volt_data, volt_lsl, volt_usl)

    fig, axes = plt.subplots(1, 3, figsize=(19, 5.5))
    fig.suptitle(f"ATE 量產測試 SPC 品質監控與 Hard Bin 分選報表 (DUT 總量: {len(df)} 顆)", fontsize=16, fontweight='bold')

    # ========================================================
    # 圖 1: 待機電流 (Idd) 直方圖
    # ========================================================
    ax1 = axes[0]
    if not curr_data.empty:
        ax1.hist(curr_data, bins=15, color='#4682B4', edgecolor='black', alpha=0.7)
        ax1.axvline(curr_lsl, color='red', linestyle='--', linewidth=2, label=f'LSL: {curr_lsl}')
        ax1.axvline(curr_usl, color='red', linestyle='--', linewidth=2, label=f'USL: {curr_usl}')
        ax1.set_title(f"靜態電流 (Idd) 分佈\nCp: {curr_stats['Cp']:.2f} | Cpk: {curr_stats['Cpk']:.2f}", fontsize=12)
        ax1.set_xlabel("電流 (mA)")
        ax1.set_ylabel("數量 (Count)")
        ax1.legend(loc="upper right")

    # ========================================================
    # 圖 2: 供電電壓 (Vbus) 直方圖 + 常態擬合曲線 (純上電數據)
    # ========================================================
    ax2 = axes[1]
    if not volt_data.empty:
        n, bins, _ = ax2.hist(volt_data, bins=15, density=True, color='#2E8B57', edgecolor='black', alpha=0.7)
        
        x_axis = np.linspace(min(volt_data) - 0.05, max(volt_data) + 0.05, 100)
        pdf = stats.norm.pdf(x_axis, volt_stats['Mean'], volt_stats['Std'])
        ax2.plot(x_axis, pdf, color='darkgreen', linewidth=2, label="Normal Fit")

        ax2.axvline(volt_lsl, color='red', linestyle='--', linewidth=2, label=f'LSL: {volt_lsl}')
        ax2.axvline(volt_usl, color='red', linestyle='--', linewidth=2, label=f'USL: {volt_usl}')
        ax2.set_title(f"供電電壓 (Vbus) 製程能力\nCp: {volt_stats['Cp']:.2f} | Cpk: {volt_stats['Cpk']:.2f}", fontsize=12)
        ax2.set_xlabel("電壓 (V)")
        ax2.set_ylabel("機率密度 (Density)")
        ax2.legend(loc="upper left")

    # ========================================================
    # 圖 3: Hard Bin 缺陷分選圓環圖 + 獨立圖例 (避免文字擁擠)
    # ========================================================
    ax3 = axes[2]
    bin_counts = df["Bin_Name"].value_counts()
    
    color_map = {
        "Bin1_Pass": "#32CD32",          # 綠色
        "Bin2_LeakageFail": "#FF8C00",   # 橘色
        "Bin3_VoltFail": "#DC143C",      # 紅色
        "Bin4_TotalFail": "#8B0000",     # 深暗紅
        "Bin5_OsFail": "#9370DB"         # 紫色
    }
    slice_colors = [color_map.get(b_name, "#A9A9A9") for b_name in bin_counts.index]

    # 製作圓環圖 (Donut Chart)，扇形內不寫長文字，只標百分比
    wedges, texts, autotexts = ax3.pie(
        bin_counts, 
        labels=None, # 不把字硬塞在扇形邊緣
        autopct='%1.1f%%', 
        pctdistance=0.8,
        startangle=140, 
        colors=slice_colors,
        wedgeprops=dict(width=0.4, edgecolor='white', linewidth=2)
    )
    for at in autotexts:
        at.set_fontsize(9)
        at.set_fontweight('bold')
    
    # 建立清楚的右側圖例 (標明名稱與顆數)
    legend_labels = [f"{b_name} ({bin_counts[b_name]} 顆)" for b_name in bin_counts.index]
    ax3.legend(wedges, legend_labels, title="Hard Bin 分類明細", loc="center left", bbox_to_anchor=(1, 0, 0.5, 1))

    yield_rate = (len(df[df["Result"] == "PASS"]) / len(df)) * 100
    ax3.set_title(f"Hard Bin 分選佔比 (總良率: {yield_rate:.1f}%)", fontsize=12, fontweight='bold')

    plt.tight_layout()
    plt.savefig("spc_binning_dashboard.png", dpi=300, bbox_inches='tight')
    print("\n[成功] 已產出高解析度 SPC 監控儀表板: 'spc_binning_dashboard.png'")
    plt.show()

if __name__ == "__main__":
    df_data = parse_log_files(LOG_FOLDER)
    if not df_data.empty:
        print("\n" + "="*50)
        print("          半導體 ATE 產線品質總結摘要")
        print("="*50)
        print(f"總測試顆數      : {len(df_data)} 顆")
        print(f"PASS 數量       : {len(df_data[df_data['Result'] == 'PASS'])} 顆")
        print(f"FAIL 數量       : {len(df_data[df_data['Result'] == 'FAIL'])} 顆")
        print(f"總良率 (Yield)  : {(len(df_data[df_data['Result'] == 'PASS'])/len(df_data))*100:.2f} %")
        print("-"*50)
        print("Hard Bin 分選明細統計:")
        print(df_data["Bin_Name"].value_counts())
        print("="*50)

        generate_spc_dashboard(df_data)