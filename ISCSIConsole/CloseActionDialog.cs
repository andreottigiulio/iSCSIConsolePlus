/* This file is part of iSCSI Console Plus, distributed under the LGPL v3 (see License.txt). */
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ISCSIConsole
{
    /// <summary>
    /// Asks what to do when the window is closed with the X button, the first time.
    /// Minimizing to the notification area is the default choice.
    /// </summary>
    public class CloseActionDialog : Form
    {
        private RadioButton radMinimize;
        private RadioButton radClose;
        private CheckBox chkRemember;
        private Button btnOk;
        private Button btnCancel;

        /// <summary>
        /// True when the user chose to keep the program running in the notification area.
        /// </summary>
        public bool MinimizeToTray
        {
            get
            {
                return radMinimize.Checked;
            }
        }

        /// <summary>
        /// True when the choice should be stored and not asked again.
        /// </summary>
        public bool RememberChoice
        {
            get
            {
                return chkRemember.Checked;
            }
        }

        public CloseActionDialog()
        {
            this.Text = "iSCSI Console";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(440, 188);
            this.SuspendLayout();

            Label lblMessage = new Label();
            lblMessage.Text = "You are closing iSCSI Console. While the program runs, the virtual disk is exported to the Windows initiator.";
            lblMessage.Location = new Point(14, 12);
            lblMessage.Size = new Size(412, 34);
            this.Controls.Add(lblMessage);

            radMinimize = new RadioButton();
            radMinimize.Text = "Minimize to the notification area and keep the server running";
            radMinimize.Location = new Point(14, 56);
            radMinimize.Size = new Size(412, 20);
            radMinimize.Checked = true;
            radMinimize.TabIndex = 0;
            this.Controls.Add(radMinimize);

            radClose = new RadioButton();
            radClose.Text = "Close the program and stop the server";
            radClose.Location = new Point(14, 80);
            radClose.Size = new Size(412, 20);
            radClose.TabIndex = 1;
            this.Controls.Add(radClose);

            chkRemember = new CheckBox();
            chkRemember.Text = "Remember my choice";
            chkRemember.Location = new Point(14, 110);
            chkRemember.Size = new Size(412, 20);
            chkRemember.Checked = true;
            chkRemember.TabIndex = 2;
            this.Controls.Add(chkRemember);

            btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.Location = new Point(269, 146);
            btnOk.Size = new Size(75, 27);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.TabIndex = 3;
            this.Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(351, 146);
            btnCancel.Size = new Size(75, 27);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.TabIndex = 4;
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            this.ResumeLayout(false);
        }
    }
}
