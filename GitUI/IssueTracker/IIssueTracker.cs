using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GitUI.IssueTracker
{
    public interface IIssueTracker
    {
        string Url { get; set; }
        List<string> GetUserItems(string UserName);
        void Init();
    }
}
