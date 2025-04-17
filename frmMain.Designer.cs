namespace WebSocketMinimalTest
{
    partial class frmMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            this.btnStarStopCycle = new System.Windows.Forms.Button();
            this.lblInfo = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnStarStopCycle
            // 
            this.btnStarStopCycle.Location = new System.Drawing.Point(12, 12);
            this.btnStarStopCycle.Name = "btnStarStopCycle";
            this.btnStarStopCycle.Size = new System.Drawing.Size(417, 71);
            this.btnStarStopCycle.TabIndex = 0;
            this.btnStarStopCycle.Text = "button1";
            this.btnStarStopCycle.UseVisualStyleBackColor = true;
            this.btnStarStopCycle.Click += new System.EventHandler(this.btnStarStopCycle_Click);
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = true;
            this.lblInfo.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblInfo.Location = new System.Drawing.Point(15, 93);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(406, 39);
            this.lblInfo.TabIndex = 1;
            this.lblInfo.Text = "When the button is enabled the WebSocket TCI connection is made.\r\nClicking will s" +
    "end the spot 10 times a second, cycling through colours,\r\nat frequnecy 14.040MHz" +
    ". This is just a demo ;)  73  - MW0LGE";
            // 
            // frmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(441, 152);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.btnStarStopCycle);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(457, 191);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(457, 191);
            this.Name = "frmMain";
            this.Text = "LGEWebSocketLite Tester - (c) 2025 - MW0LGE";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnStarStopCycle;
        private System.Windows.Forms.Label lblInfo;
    }
}

