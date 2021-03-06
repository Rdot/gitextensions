﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GitCommands;
using GitCommands.Repository;
using GitUI.RepoHosting;
using GitUI.Script;
using ResourceManager.Translation;
using GitCommands.Config;

namespace GitUI
{
    public partial class FormPush : GitModuleForm
    {
        private const string PuttyText = "PuTTY";
        private const string HeadText = "HEAD";
        private string _currentBranch;
        private string _currentBranchRemote;
        private bool candidateForRebasingMergeCommit;
        private string selectedBranch;
        private string selectedBranchRemote;
        private string selectedRemoteBranchName;


        #region Translation
        private readonly TranslationString _branchNewForRemote =
            new TranslationString("The branch you are about to push seems to be a new branch for the remote." +
                                  Environment.NewLine + "Are you sure you want to push this branch?");

        private readonly TranslationString _cannotLoadPutty =
            new TranslationString("Cannot load SSH key. PuTTY is not configured properly.");

        private readonly TranslationString _pushCaption = new TranslationString("Push");

        private readonly TranslationString _pushToCaption = new TranslationString("Push to {0}");

        private readonly TranslationString _selectDestinationDirectory =
            new TranslationString("Please select a destination directory");

        private readonly TranslationString _selectRemote = new TranslationString("Please select a remote repository");

        private readonly TranslationString _selectTag =
            new TranslationString("You need to select a tag to push or select \"Push all tags\".");

        private readonly TranslationString _updateTrackingReference =
            new TranslationString("The branch {0} does not have a tracking reference. Do you want to add a tracking reference to {1}?");

        private readonly TranslationString _yes = new TranslationString("Yes");
        private readonly TranslationString _no = new TranslationString("No");
        #endregion

        private FormPush()
            : this(null)
        { }

        public FormPush(GitUICommands aCommands)
            : base(aCommands)
        {
            InitializeComponent();
            Translate();

            //can't be set in OnLoad, because after PushAndShowDialogWhenFailed()
            //they are reset to false
            PushAllTags.Checked = Settings.PushAllTags;
            AutoPullOnRejected.Checked = Settings.AutoPullOnRejected;
            if (aCommands != null)
                Init();
        }

        private void Init()
        {
            if (GitCommandHelpers.VersionInUse.SupportPushWithRecursiveSubmodulesCheck)
            {
                RecursiveSubmodules.Enabled = true;
                RecursiveSubmodules.SelectedIndex = Settings.RecursiveSubmodules;
                if (!GitCommandHelpers.VersionInUse.SupportPushWithRecursiveSubmodulesOnDemand)
                    RecursiveSubmodules.Items.RemoveAt(2);
            }
            else
            {
                RecursiveSubmodules.Enabled = false;
                RecursiveSubmodules.SelectedIndex = 0;
            }

            _currentBranch = Module.GetSelectedBranch();

            _NO_TRANSLATE_Remotes.DataSource = Module.GetRemotes();

            UpdateBranchDropDown();
            UpdateRemoteBranchDropDown();

            Push.Focus();

            _currentBranchRemote = Module.GetSetting(string.Format("branch.{0}.remote", _currentBranch));
            if (_currentBranchRemote.IsNullOrEmpty() && _NO_TRANSLATE_Remotes.Items.Count >= 2)
            {
                IList<string> remotes = (IList<string>)_NO_TRANSLATE_Remotes.DataSource;
                int i = remotes.IndexOf("origin");
                _NO_TRANSLATE_Remotes.SelectedIndex = i >= 0 ? i : 0;
            }
            else
                _NO_TRANSLATE_Remotes.Text = _currentBranchRemote;
            RemotesUpdated(null, null);
        }

        public void PushAndShowDialogWhenFailed(IWin32Window owner)
        {
            if (!PushChanges(owner))
                ShowDialog(owner);
        }

        public void PushAndShowDialogWhenFailed()
        {
            PushAndShowDialogWhenFailed(null);
        }

        private void PushClick(object sender, EventArgs e)
        {
            if (PushChanges(this))
                Close();
        }

        private string GetDefaultPushLocal(String remote)
        {
            string localRef = null;

            //Get default push for this remote (if any). Local branch name is left of ":"
            var pushSettingValue = Module.GetSetting(string.Format("remote.{0}.push", remote));
            if (!string.IsNullOrEmpty(pushSettingValue))
            {
                var values = pushSettingValue.Split(':');
                if (values.Length > 0)
                    localRef = values[0];
            }
            return localRef;
        }

        private string GetDefaultPushRemote(String remote)
        {
            string remoteRef = null;

            //Get default push for this remote (if any). Remote branch name is right of ":"
            var pushSettingValue = Module.GetSetting(string.Format("remote.{0}.push", remote));
            if (!string.IsNullOrEmpty(pushSettingValue))
            {
                var values = pushSettingValue.Split(':');
                if (values.Length > 1)
                    remoteRef = values[1];
            }

            return remoteRef;
        }

        private bool PushChanges(IWin32Window owner)
        {
            if (PushToUrl.Checked && string.IsNullOrEmpty(PushDestination.Text))
            {
                MessageBox.Show(owner, _selectDestinationDirectory.Text);
                return false;
            }
            if (PushToRemote.Checked && string.IsNullOrEmpty(_NO_TRANSLATE_Remotes.Text))
            {
                MessageBox.Show(owner, _selectRemote.Text);
                return false;
            }
            if (TabControlTagBranch.SelectedTab == TagTab && string.IsNullOrEmpty(TagComboBox.Text) &&
                !PushAllTags.Checked)
            {
                MessageBox.Show(owner, _selectTag.Text);
                return false;
            }

            //Extra check if the branch is already known to the remote, give a warning when not.
            //This is not possible when the remote is an URL, but this is ok since most users push to
            //known remotes anyway.
            if (TabControlTagBranch.SelectedTab == BranchTab && PushToRemote.Checked)
            {
                //If the current branch is not the default push, and not known by the remote 
                //(as far as we know since we are disconnected....)
                if (RemoteBranch.Text != GetDefaultPushRemote(_NO_TRANSLATE_Remotes.Text) &&
                    !Module.GetHeads(true, true).Any(x => x.Remote == _NO_TRANSLATE_Remotes.Text && x.LocalName == RemoteBranch.Text) )
                    //Ask if this is really what the user wants
                    if (MessageBox.Show(owner, _branchNewForRemote.Text, _pushCaption.Text, MessageBoxButtons.YesNo) ==
                        DialogResult.No)
                    {
                        return false;
                    }
            }

            if (PushToUrl.Checked)
                Repositories.AddMostRecentRepository(PushDestination.Text);
            Settings.PushAllTags = PushAllTags.Checked;
            Settings.AutoPullOnRejected = AutoPullOnRejected.Checked;
            Settings.RecursiveSubmodules = RecursiveSubmodules.SelectedIndex;

            var remote = "";
            string destination;
            if (PushToUrl.Checked)
            {
                destination = PushDestination.Text;
            }
            else
            {
                if (GitCommandHelpers.Plink())
                {
                    if (!File.Exists(Settings.Pageant))
                        MessageBox.Show(owner, _cannotLoadPutty.Text, PuttyText);
                    else
                        Module.StartPageantForRemote(_NO_TRANSLATE_Remotes.Text);
                }

                destination = _NO_TRANSLATE_Remotes.Text;
                remote = _NO_TRANSLATE_Remotes.Text.Trim();
            }

            string pushCmd;
            if (TabControlTagBranch.SelectedTab == BranchTab)
            {
                bool track = ReplaceTrackingReference.Checked;
                if (!track && !string.IsNullOrWhiteSpace(RemoteBranch.Text))
                {
                    GitHead selectedLocalBranch = _NO_TRANSLATE_Branch.SelectedItem as GitHead;
                    track = selectedLocalBranch != null && string.IsNullOrEmpty(selectedLocalBranch.TrackingRemote);

                    string[] remotes = _NO_TRANSLATE_Remotes.DataSource as string[];
                    if (remotes != null)
                        foreach (string remoteBranch in remotes)
                            if (!string.IsNullOrEmpty(remoteBranch) && _NO_TRANSLATE_Branch.Text.StartsWith(remoteBranch))
                                track = false;

                    if (track)
                    {
                        DialogResult result = MessageBox.Show(String.Format(_updateTrackingReference.Text, selectedLocalBranch.Name, RemoteBranch.Text), _pushCaption.Text, MessageBoxButtons.YesNoCancel);

                        if (result == DialogResult.Cancel)
                            return false;

                        track = result == DialogResult.Yes;
                    }
                }

                pushCmd = GitCommandHelpers.PushCmd(destination, _NO_TRANSLATE_Branch.Text, RemoteBranch.Text,
                    PushAllBranches.Checked, ForcePushBranches.Checked, track, RecursiveSubmodules.SelectedIndex);
            }
            else if (TabControlTagBranch.SelectedTab == TagTab)
                pushCmd = GitCommandHelpers.PushTagCmd(destination, TagComboBox.Text, PushAllTags.Checked,
                                                             ForcePushBranches.Checked);
            else
            {
                // Push Multiple Branches Tab selected
                var pushActions = new List<GitPushAction>();
                foreach (DataRow row in _branchTable.Rows)
                {
                    var push = Convert.ToBoolean(row["Push"]);
                    var force = Convert.ToBoolean(row["Force"]);
                    var delete = Convert.ToBoolean(row["Delete"]);

                    if (push || force)
                        pushActions.Add(new GitPushAction(row["Local"].ToString(), row["Remote"].ToString(), force));
                    else if (delete)
                        pushActions.Add(new GitPushAction(row["Remote"].ToString()));
                }
                pushCmd = GitCommandHelpers.PushMultipleCmd(destination, pushActions);
            }

            ScriptManager.RunEventScripts(Module, ScriptEvent.BeforePush);

            //controls can be accessed only from UI thread
            candidateForRebasingMergeCommit = Settings.PullMerge == Settings.PullAction.Rebase && PushToRemote.Checked && !PushAllBranches.Checked && TabControlTagBranch.SelectedTab == BranchTab;
            selectedBranch = _NO_TRANSLATE_Branch.Text;
            selectedBranchRemote = _NO_TRANSLATE_Remotes.Text;
            selectedRemoteBranchName = RemoteBranch.Text;

            using (var form = new FormRemoteProcess(Module, pushCmd)
                       {
                           Remote = remote,
                           Text = string.Format(_pushToCaption.Text, destination),
                           HandleOnExitCallback = HandlePushOnExit
                       })
            {

                form.ShowDialog(owner);

                if (!Module.InTheMiddleOfConflictedMerge() &&
                    !Module.InTheMiddleOfRebase() && !form.ErrorOccurred())
                {
                    ScriptManager.RunEventScripts(Module, ScriptEvent.AfterPush);
                    if (_createPullRequestCB.Checked)
                        UICommands.StartCreatePullRequest(owner);
                    return true;
                }
            }

            return false;
        }


        private bool IsRebasingMergeCommit()
        {
            if (candidateForRebasingMergeCommit)
            {
                if (selectedBranch == _currentBranch && selectedBranchRemote == _currentBranchRemote)
                {
                    string remoteBranchName = selectedBranchRemote + "/" + selectedRemoteBranchName;
                    return Module.ExistsMergeCommit(remoteBranchName, selectedBranch);
                }
                else
                    return false;
            }
            else
                return false;
        }

        private bool HandlePushOnExit(ref bool isError, FormProcess form)
        {
            if (isError)
            {
                //auto pull only if current branch was rejected
                Regex IsRejected = new Regex(Regex.Escape("! [rejected] ") + ".*" + Regex.Escape(_currentBranch) + ".*" + Regex.Escape(" (non-fast-forward)"), RegexOptions.Compiled);

                if (Settings.AutoPullOnRejected && IsRejected.IsMatch(form.OutputString.ToString()))
                    
                {
                    if (Settings.PullMerge == Settings.PullAction.Fetch)
                        form.AppendOutputLine(Environment.NewLine + "Can not perform auto pull, when merge option is set to fetch.");
                    else if (IsRebasingMergeCommit())
                        form.AppendOutputLine(Environment.NewLine + "Can not perform auto pull, when merge option is set to rebase " + Environment.NewLine
                                            + "and one of the commits that are about to be rebased is a merge.");
                    else
                    {
                        form.Visible = false;
                        bool pullCompleted;
                        UICommands.StartPullDialog(form, true, out pullCompleted);
                        if (pullCompleted)
                        {
                            form.Visible = true;
                            form.Retry();
                            return true;
                        }
                        form.Visible = true;
                    }
                }
            }

            return false;
        }

        private void FillPushDestinationDropDown()
        {
            PushDestination.DataSource = Repositories.RemoteRepositoryHistory.Repositories;
            PushDestination.DisplayMember = "Path";
        }

        private void UpdateBranchDropDown()
        {
            var curBranch = _NO_TRANSLATE_Branch.Text;

            _NO_TRANSLATE_Branch.DisplayMember = "Name";
            _NO_TRANSLATE_Branch.Items.Clear();
            _NO_TRANSLATE_Branch.Items.Add(HeadText);

            if (string.IsNullOrEmpty(curBranch))
            {
                curBranch = _currentBranch;
                if (curBranch.IndexOfAny("() ".ToCharArray()) != -1)
                    curBranch = HeadText;
            }

            foreach (var head in Module.GetHeads(false, true))
                _NO_TRANSLATE_Branch.Items.Add(head);

            _NO_TRANSLATE_Branch.Text = curBranch;
        }

        private void PullClick(object sender, EventArgs e)
        {
            UICommands.StartPullDialog(this);
        }

        private void UpdateRemoteBranchDropDown()
        {
            RemoteBranch.DisplayMember = "Name";
            RemoteBranch.Items.Clear();

            if (!string.IsNullOrEmpty(_NO_TRANSLATE_Branch.Text))
                RemoteBranch.Items.Add(_NO_TRANSLATE_Branch.Text);

            foreach (var head in Module.GetHeads(false, true))
                if (!RemoteBranch.Items.Contains(head))
                    RemoteBranch.Items.Add(head);
        }

        private void BranchSelectedValueChanged(object sender, EventArgs e)
        {
            if (_NO_TRANSLATE_Branch.Text != HeadText)
            {
                if (PushToRemote.Checked)
                {
                    var branch = _NO_TRANSLATE_Branch.SelectedItem as GitHead;
                    if (branch != null && branch.TrackingRemote.Equals(_NO_TRANSLATE_Remotes.Text.Trim()))
                    {
                        RemoteBranch.Text = branch.MergeWith;
                        if (!string.IsNullOrEmpty(RemoteBranch.Text))
                            return;
                    }
                }

                RemoteBranch.Text = _NO_TRANSLATE_Branch.Text;
            }
        }

        private void FormPushLoad(object sender, EventArgs e)
        {
            _NO_TRANSLATE_Remotes.Select();

            Text = string.Concat(_pushCaption.Text, " (", Module.WorkingDir, ")");

            var gitHoster = RepoHosts.TryGetGitHosterForModule(Module);
            _createPullRequestCB.Enabled = gitHoster != null;
        }

        private void AddRemoteClick(object sender, EventArgs e)
        {
            UICommands.StartRemotesDialog(this, _NO_TRANSLATE_Remotes.Text);
            string origText = _NO_TRANSLATE_Remotes.Text;
            _NO_TRANSLATE_Remotes.DataSource = Module.GetRemotes();
            if (_NO_TRANSLATE_Remotes.Items.Contains(origText)) // else first item gets selected
            {
                _NO_TRANSLATE_Remotes.Text = origText;
            }
        }

        private void PushToRemoteCheckedChanged(object sender, EventArgs e)
        {
            BranchSelectedValueChanged(null, null);
            if (!PushToRemote.Checked)
                return;

            PushDestination.Enabled = false;
            folderBrowserButton1.Enabled = false;
            _NO_TRANSLATE_Remotes.Enabled = true;
            AddRemote.Enabled = true;
        }

        private void PushToUrlCheckedChanged(object sender, EventArgs e)
        {
            if (!PushToUrl.Checked)
                return;

            PushDestination.Enabled = true;
            folderBrowserButton1.Enabled = true;
            _NO_TRANSLATE_Remotes.Enabled = false;
            AddRemote.Enabled = false;

            FillPushDestinationDropDown();
        }

        private void RemotesUpdated(object sender, EventArgs e)
        {
            if (TabControlTagBranch.SelectedTab == MultipleBranchTab)
                UpdateMultiBranchView();

            EnableLoadSshButton();

            // update the text box of the Remote Url combobox to show the URL of selected remote
            {
                string pushUrl = Module.GetPathSetting(string.Format(SettingKeyString.RemotePushUrl, _NO_TRANSLATE_Remotes.Text));
                if (pushUrl.IsNullOrEmpty())
                {
                    pushUrl = Module.GetPathSetting(string.Format(SettingKeyString.RemoteUrl, _NO_TRANSLATE_Remotes.Text));
                }
                PushDestination.Text = pushUrl;
            }

            var pushSettingValue = Module.GetSetting(string.Format("remote.{0}.push", _NO_TRANSLATE_Remotes.Text));

            if (PushToRemote.Checked && !string.IsNullOrEmpty(pushSettingValue))
            {
                string defaultLocal = GetDefaultPushLocal(_NO_TRANSLATE_Remotes.Text);
                string defaultRemote = GetDefaultPushRemote(_NO_TRANSLATE_Remotes.Text);

                RemoteBranch.Text = "";
                if (!string.IsNullOrEmpty(defaultLocal))
                {
                    var currentBranch = new GitHead(Module, null, defaultLocal, _NO_TRANSLATE_Remotes.Text);
                    _NO_TRANSLATE_Branch.Items.Add(currentBranch);
                    _NO_TRANSLATE_Branch.SelectedItem = currentBranch;
                }
                if (!string.IsNullOrEmpty(defaultRemote))
                    RemoteBranch.Text = defaultRemote;
                return;
            }

            if (string.IsNullOrEmpty(_NO_TRANSLATE_Branch.Text))
            {
                // Doing this makes it pretty easy to accidentally create a branch on the remote.
                // But leaving it blank will do the 'default' thing, meaning all branches are pushed.
                // Solution: when pushing a branch that doesn't exist on the remote, ask what to do
                var currentBranch = new GitHead(Module, null, _currentBranch, _NO_TRANSLATE_Remotes.Text);
                _NO_TRANSLATE_Branch.Items.Add(currentBranch);
                _NO_TRANSLATE_Branch.SelectedItem = currentBranch;
                return;
            }

            BranchSelectedValueChanged(null, null);
        }

        private void EnableLoadSshButton()
        {
            LoadSSHKey.Visible = !string.IsNullOrEmpty(Module.GetPuttyKeyFileForRemote(_NO_TRANSLATE_Remotes.Text));
        }

        private void LoadSshKeyClick(object sender, EventArgs e)
        {
            if (!File.Exists(Settings.Pageant))
                MessageBox.Show(this, _cannotLoadPutty.Text, PuttyText);
            else
                Module.StartPageantForRemote(_NO_TRANSLATE_Remotes.Text);
        }

        private void RemotesValidated(object sender, EventArgs e)
        {
            EnableLoadSshButton();
        }

        private void FillTagDropDown()
        {
            TagComboBox.DisplayMember = "Name";
            /// var tags = Module.GetTagHeads(GitModule.GetTagHeadsOption.OrderByCommitDateDescending); // comment out to sort by commit date
            var tags = Module.GetTagHeads(GitModule.GetTagHeadsSortOrder.ByName);
            TagComboBox.DataSource = tags;
        }

        private void ForcePushBranchesCheckedChanged(object sender, EventArgs e)
        {
            ForcePushTags.Checked = ForcePushBranches.Checked;
        }

        private void ForcePushTagsCheckedChanged(object sender, EventArgs e)
        {
            ForcePushBranches.Checked = ForcePushTags.Checked;
        }

        private void PushAllBranchesCheckedChanged(object sender, EventArgs e)
        {
            _NO_TRANSLATE_Branch.Enabled = !PushAllBranches.Checked;
            RemoteBranch.Enabled = !PushAllBranches.Checked;
        }

        #region Multi-Branch Methods

        private DataTable _branchTable;

        private void UpdateMultiBranchView()
        {
            _branchTable = new DataTable();
            _branchTable.Columns.Add("Local", typeof (string));
            _branchTable.Columns.Add("Remote", typeof (string));
            _branchTable.Columns.Add("New", typeof (string));
            _branchTable.Columns.Add("Push", typeof(bool));
            _branchTable.Columns.Add("Force", typeof(bool));
            _branchTable.Columns.Add("Delete", typeof(bool));
            _branchTable.ColumnChanged += BranchTable_ColumnChanged;
            var bs = new BindingSource {DataSource = _branchTable};
            BranchGrid.DataSource = bs;

            string remote = _NO_TRANSLATE_Remotes.Text.Trim();
            if (remote == "")
                return;

            var localHeads = Module.GetHeads(false, true);
            var remoteHeads = Module.GetRemoteHeads(remote, false, true);

            // Add all the local branches.
            foreach (var head in localHeads)
            {
                DataRow row = _branchTable.NewRow();
                row["Force"] = false;
                row["Delete"] = false;
                row["Local"] = head.Name;

                string remoteName;
                if (head.Remote == remote)
                    remoteName = head.MergeWith ?? head.Name;
                else
                    remoteName = head.Name;

                row["Remote"] = remoteName;
                bool newAtRemote = remoteHeads.Any(h => h.Name == remoteName);
                row["New"] =  newAtRemote ? _no.Text : _yes.Text;
                row["Push"] = newAtRemote;

                _branchTable.Rows.Add(row);
            }

            // Offer to delete all the left over remote branches.
            foreach (var remoteHead in remoteHeads)
            {
                GitHead head = remoteHead;
                if (!localHeads.Any(h => h.Name == head.Name))
                {
                    DataRow row = _branchTable.NewRow();
                    row["Local"] = null;
                    row["Remote"] = remoteHead.Name;
                    row["New"] = _no.Text;
                    row["Push"] = false;
                    row["Force"] = false;
                    row["Delete"] = false;
                    _branchTable.Rows.Add(row);
                }
            }
        }

        static void BranchTable_ColumnChanged(object sender, DataColumnChangeEventArgs e)
        {
            if (e.Column.ColumnName == "Push" && (bool)e.ProposedValue)
            {
                e.Row["Force"] = false;
                e.Row["Delete"] = false;
            }
            if (e.Column.ColumnName == "Force" && (bool)e.ProposedValue)
            {
                e.Row["Push"] = false;
                e.Row["Delete"] = false;
            }
            if (e.Column.ColumnName == "Delete" && (bool)e.ProposedValue)
            {
                e.Row["Push"] = false;
                e.Row["Force"] = false;
            }
        }

        private void TabControlTagBranch_Selected(object sender, TabControlEventArgs e)
        {
            if (TabControlTagBranch.SelectedTab == MultipleBranchTab)
                UpdateMultiBranchView();
            else if (TabControlTagBranch.SelectedTab == TagTab)
                FillTagDropDown();
            else
            {
                UpdateBranchDropDown();
                UpdateRemoteBranchDropDown();
            }
        }

        private void BranchGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // Push grid checkbox changes immediately into the underlying data table.
            if (BranchGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                BranchGrid.EndEdit();
                ((BindingSource)BranchGrid.DataSource).EndEdit();
            }
        }

        #endregion


        private void ShowOptions_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PushOptionsPanel.Visible = true;
            ShowOptions.Visible = false;
            SetFormSizeToFitAllItems();
        }

        private void ShowTagOptions_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            TagOptionsPanel.Visible = true;
            ShowTagOptions.Visible = false;
            SetFormSizeToFitAllItems();
        }

        private void SetFormSizeToFitAllItems()
        {
            this.Size = new System.Drawing.Size(this.MinimumSize.Width, this.MinimumSize.Height + 70);
        }
    }
}