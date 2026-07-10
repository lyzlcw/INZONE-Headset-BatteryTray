namespace MixTray;

public class SchemeSelectorDialog : Form
{
    public int SelectedScheme { get; private set; }

    public SchemeSelectorDialog()
    {
        Text = "选择电量读取方案";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 290);

        var lblTitle = new Label
        {
            Text = "请选择 INZONE H9 电量读取方案：",
            Location = new Point(20, 15),
            Size = new Size(480, 28),
            Font = new Font("Segoe UI", 11)
        };

        var rbClrMd = new RadioButton
        {
            Text = "InzoneBatteryTray（ClrMD 方案）",
            Location = new Point(24, 62),
            Size = new Size(470, 28),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Checked = true,
            AutoSize = false
        };

        var lblDesc1 = new Label
        {
            Text = "通过读取 INZONE Hub 进程内存获取电量，需要 Hub 保持后台运行",
            Location = new Point(46, 92),
            Size = new Size(450, 20),
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = Color.Gray,
            AutoSize = false
        };

        var separator = new Label
        {
            Location = new Point(24, 120),
            Size = new Size(472, 1),
            BorderStyle = BorderStyle.FixedSingle
        };

        var rbUsb = new RadioButton
        {
            Text = "InzoneUsbTray（USB CDC 方案）",
            Location = new Point(24, 133),
            Size = new Size(470, 28),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = false
        };

        var lblDesc2 = new Label
        {
            Text = "通过 USB 直接读取耳机电量，无需 Hub，但会占用 COM4 端口并关闭 Hub",
            Location = new Point(46, 163),
            Size = new Size(450, 20),
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = Color.Gray,
            AutoSize = false
        };

        var btnOk = new Button
        {
            Text = "确定",
            Location = new Point(165, 215),
            Size = new Size(85, 28),
            DialogResult = DialogResult.OK
        };
        var btnCancel = new Button
        {
            Text = "退出",
            Location = new Point(270, 215),
            Size = new Size(85, 28),
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(lblTitle);
        Controls.Add(rbClrMd);
        Controls.Add(lblDesc1);
        Controls.Add(separator);
        Controls.Add(rbUsb);
        Controls.Add(lblDesc2);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        rbClrMd.Click += (_, _) => { rbClrMd.Checked = true; rbUsb.Checked = false; };
        rbUsb.Click += (_, _) => { rbClrMd.Checked = false; rbUsb.Checked = true; };

        btnOk.Click += (_, _) =>
        {
            SelectedScheme = rbClrMd.Checked ? 0 : 1;
            Close();
        };
    }
}
