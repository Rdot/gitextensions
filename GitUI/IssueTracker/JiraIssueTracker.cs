using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.IO;
using GitUI.JiraService;
using GitCommands;

namespace GitUI.IssueTracker
{
    public class JiraIssueTracker : BaseIssueTracker
    {
        public override List<string> GetUserItems(string UserName)
        {
            List<string> listIssues = new List<string>();
            try
            {
                using (JiraService.JiraSoapServiceService svc = new JiraService.JiraSoapServiceService())
                {
                    svc.Url = Settings.IssueServiceUrl;

                    string sStatusToShow = "";

                    if (Settings.ShowStatusInProgress)
                    {
                        sStatusToShow = "status = 'in progress'";
                    }

                    if (Settings.ShowStatusOpen)
                    {
                        if (sStatusToShow.Length == 0)
                        {
                            sStatusToShow ="status='open'";
                        }
                        else
                        {
                            sStatusToShow += " or status='open'";
                        }
                    }

                    if (Settings.ShowStatusResolved)
                    {
                        if (sStatusToShow.Length == 0)
                        {
                            sStatusToShow ="status='resolved'";
                        }
                        else
                        {
                            sStatusToShow += " or status='resolved'";
                        }
                    }
                    sStatusToShow = "( " + sStatusToShow + " )";

                    string sLoginToken = svc.login(Settings.IssueServiceUserName,Settings.IssueServicePassword);
                    RemoteIssue [] issues = svc.getIssuesFromJqlSearch(sLoginToken,"assignee='" + UserName + "' and " + sStatusToShow,50);

                    foreach (RemoteIssue ri in issues)
                    {
                        listIssues.Add(ri.key);
                    }
                    svc.logout(sLoginToken);
                }
            }
            catch (System.Exception e)
            {
                listIssues.Insert(0,"Error loading issues");
            }

            return listIssues;
        }
    }
}
