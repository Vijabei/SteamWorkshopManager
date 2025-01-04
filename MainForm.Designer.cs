using System;
using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;

namespace WorkshopManager
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        
        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        protected virtual void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            
            // Basis-Formular-Eigenschaften
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.Name = "MainForm";
            this.Text = "Workshop Mod Manager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            
            // Initialisiere die Container für Controls
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();
            
            // Konfiguriere das Form für programmatisches UI
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}