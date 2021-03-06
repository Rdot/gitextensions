using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GitCommands;
using PatchApply;
using ResourceManager.Translation;

namespace GitUI
{
    public sealed partial class FormStash : GitModuleForm
    {
        readonly TranslationString currentWorkingDirChanges = new TranslationString("Current working dir changes");
        readonly TranslationString noStashes = new TranslationString("There are no stashes.");
        readonly TranslationString stashUntrackedFilesNotSupportedCaption = new TranslationString("Stash untracked files");
        readonly TranslationString stashUntrackedFilesNotSupported = new TranslationString("Stash untracked files is not supported in the version of msysgit you are using. Please update msysgit to at least version 1.7.7 to use this option.");
        readonly TranslationString stashDropConfirmTitle = new TranslationString("Drop Stash Confirmation");
        readonly TranslationString cannotBeUndone = new TranslationString("This action cannot be undone.");
        readonly TranslationString areYouSure = new TranslationString("Are you sure you want to drop the stash? This action cannot be undone.");
        readonly TranslationString dontShowAgain = new TranslationString("Don't show me this message again.");
        public bool NeedRefresh;

        private FormStash()
            : this(null)
        { }

        public FormStash(GitUICommands aCommands)
            : base(aCommands)
        {
            InitializeComponent();
            Loading.Image = global::GitUI.Properties.Resources.loadingpanel;
            Translate();
            View.ExtraDiffArgumentsChanged += ViewExtraDiffArgumentsChanged;
        }

        private void ViewExtraDiffArgumentsChanged(object sender, EventArgs e)
        {
            StashedSelectedIndexChanged(null, null);
        }

        private void FormStashFormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.StashKeepIndex = StashKeepIndex.Checked;
            Settings.IncludeUntrackedFilesInManualStash = chkIncludeUntrackedFiles.Checked;
        }

        private void FormStashLoad(object sender, EventArgs e)
        {
            StashKeepIndex.Checked = Settings.StashKeepIndex;
            chkIncludeUntrackedFiles.Checked = Settings.IncludeUntrackedFilesInManualStash;

            splitContainer2_SplitterMoved(null, null);
        }

        GitStash currentWorkingDirStashItem;

        private void Initialize()
        {
            IList<GitStash> stashedItems = Module.GetStashes();

            currentWorkingDirStashItem = new GitStash
            {
                Name = currentWorkingDirChanges.Text,
                Message = currentWorkingDirChanges.Text
            };

            stashedItems.Insert(0, currentWorkingDirStashItem);

            Stashes.Text = "";
            StashMessage.Text = "";
            Stashes.SelectedItem = null;
            Stashes.Items.Clear();
            foreach (GitStash stashedItem in stashedItems)
                Stashes.Items.Add(stashedItem);
            if (Stashes.Items.Count > 1)
                Stashes.SelectedIndex = 1;
            else if (Stashes.Items.Count > 0)
                Stashes.SelectedIndex = 0;
        }

        private void InitializeSoft()
        {
            GitStash gitStash = Stashes.SelectedItem as GitStash;

            Stashed.GitItemStatuses = null;

            Loading.Visible = true;
            Stashes.Enabled = false;
            toolStripButton1.Enabled = false;
            toolStripButton_customMessage.Enabled = false;
            if (gitStash == null)
            {
                Stashed.GitItemStatuses = null;
            }
            else if (gitStash == currentWorkingDirStashItem)
            {
                toolStripButton_customMessage.Enabled = true;
                AsyncLoader.DoAsync(() => Module.GetAllChangedFiles(), LoadGitItemStatuses);
            }
            else
                AsyncLoader.DoAsync(() => Module.GetStashDiffFiles(gitStash.Name), LoadGitItemStatuses);
        }

        private void LoadGitItemStatuses(IList<GitItemStatus> gitItemStatuses)
        {
            Stashed.GitItemStatuses = gitItemStatuses;
            Loading.Visible = false;
            Stashes.Enabled = true;
            toolStripButton1.Enabled = true;
        }

        private void StashedSelectedIndexChanged(object sender, EventArgs e)
        {
            GitStash gitStash = Stashes.SelectedItem as GitStash;
            GitItemStatus stashedItem = Stashed.SelectedItem;
            Cursor.Current = Cursors.WaitCursor;

            if (stashedItem != null &&
                gitStash == currentWorkingDirStashItem) //current working dir
            {
                View.ViewCurrentChanges(stashedItem.Name, stashedItem.OldName, stashedItem.IsStaged, stashedItem.IsSubmodule);
            }
            else if (stashedItem != null)
            {
                if (stashedItem.IsNew && !stashedItem.IsTracked)
                {
                    if (!stashedItem.IsSubmodule)
                        View.ViewGitItem(stashedItem.Name, stashedItem.TreeGuid);
                    else
                        View.ViewText(stashedItem.Name,
                            GitCommandHelpers.GetSubmoduleText(Module, stashedItem.Name, stashedItem.TreeGuid));
                }
                else 
                {
                    string extraDiffArguments = View.GetExtraDiffArguments();
                    Encoding encoding = this.View.Encoding;
                    View.ViewPatch(() =>
                    {
                        Patch patch = Module.GetSingleDiff(gitStash.Name, gitStash.Name + "^", stashedItem.Name, stashedItem.OldName, extraDiffArguments, encoding);
                        if (patch == null)
                            return String.Empty;
                        if (stashedItem.IsSubmodule)
                            return GitCommandHelpers.ProcessSubmodulePatch(Module, patch.Text);
                        return patch.Text;
                    });
                }
            }
            else
                View.ViewText(string.Empty, string.Empty);
            Cursor.Current = Cursors.Default;
        }

        private void ShowPatch(Patch patch)
        {
            View.ViewPatch(patch.Text);
        }

        private void StashClick(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            var msg = toolStripButton_customMessage.Checked ? " " + StashMessage.Text.Trim() : string.Empty;
            var arguments = string.Empty;
            if (StashKeepIndex.Checked)
                arguments += " --keep-index";

            if (chkIncludeUntrackedFiles.Checked)
            {
                if (GitCommandHelpers.VersionInUse.StashUntrackedFilesSupported)
                {
                    arguments += " -u";
                }
                else
                {
                    if (MessageBox.Show(stashUntrackedFilesNotSupported.Text, stashUntrackedFilesNotSupportedCaption.Text, MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.Cancel)
                        return;
                }
            }
            FormProcess.ShowDialog(this, String.Format("stash save {0}{1}", arguments, msg));
            NeedRefresh = true;
            Initialize();
            Cursor.Current = Cursors.Default;
        }

        private void ClearClick(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            if (Settings.StashConfirmDropShow)
            {
                DialogResult res = PSTaskDialog.cTaskDialog.MessageBox(
                                        this,
                                       stashDropConfirmTitle.Text,
                                       cannotBeUndone.Text,
                                       areYouSure.Text,
                                       "",
                                       "",
                                       dontShowAgain.Text,
                                       PSTaskDialog.eTaskDialogButtons.OKCancel,
                                       PSTaskDialog.eSysIcons.Information,
                                       PSTaskDialog.eSysIcons.Information);
                if (res == DialogResult.OK)
                {
                    FormProcess.ShowDialog(this, string.Format("stash drop \"{0}\"", Stashes.Text));
                    NeedRefresh = true;
                    Initialize();
                    Cursor.Current = Cursors.Default;
                }

                if (PSTaskDialog.cTaskDialog.VerificationChecked)
                {
                    Settings.StashConfirmDropShow = false;
                }
            }
            else
            {
                FormProcess.ShowDialog(this, string.Format("stash drop \"{0}\"", Stashes.Text));
                NeedRefresh = true;
                Initialize();
                Cursor.Current = Cursors.Default;
            }
            

        }

        private void ApplyClick(object sender, EventArgs e)
        {
            FormProcess.ShowDialog(this, string.Format("stash apply \"{0}\"", Stashes.Text));

            MergeConflictHandler.HandleMergeConflicts(UICommands, this, false);

            Initialize();
        }

        private void StashesSelectedIndexChanged(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            InitializeSoft();

            if (Stashes.SelectedItem != null)
                StashMessage.Text = ((GitStash)Stashes.SelectedItem).Message;

            if (Stashes.Items.Count == 1)
                StashMessage.Text = noStashes.Text;

            Cursor.Current = Cursors.Default;
        }

        private void RefreshClick(object sender, EventArgs e)
        {
            RefreshAll();
        }

        private void RefreshAll()
        {
            Cursor.Current = Cursors.WaitCursor;
            Initialize();
            Cursor.Current = Cursors.Default;
        }

        private void FormStashShown(object sender, EventArgs e)
        {
            RefreshAll();
        }

        private void splitContainer2_SplitterMoved(object sender, SplitterEventArgs e)
        {
            Stashes.Size = new Size(Math.Min(200, toolStrip1.Width - 25 - toolStripButton1.Width - toolStripLabel1.Width - toolStripButton_customMessage.Width), Stashes.Size.Height);
        }

        private void FormStash_Resize(object sender, EventArgs e)
        {
            splitContainer2_SplitterMoved(null, null);
        }

        private void toolStripButton_customMessage_Click(object sender, EventArgs e)
        {
            if (toolStripButton_customMessage.Enabled)
            {
                if (((ToolStripButton)sender).Checked)
                {
                    this.StashMessage.ReadOnly = false;
                    this.StashMessage.Focus();
                    this.StashMessage.SelectAll();
                }
                else
                {
                    this.StashMessage.ReadOnly = true;
                }
            }
        }

        private void StashMessage_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.toolStripButton_customMessage.Enabled)
            {
                if (!this.toolStripButton_customMessage.Checked)
                    this.toolStripButton_customMessage.PerformClick();
            }
        }

        private void toolStripButton_customMessage_EnabledChanged(object sender, EventArgs e)
        {
            var button = (ToolStripButton)sender;
            if (!button.Enabled)
            {
                StashMessage.ReadOnly = true;
            }
            else if (button.Enabled && button.Checked)
            {
                StashMessage.ReadOnly = false;
            }
        }

    }
}