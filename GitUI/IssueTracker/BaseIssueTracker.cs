using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GitUI.IssueTracker
{
    public abstract class BaseIssueTracker : IIssueTracker
    {
        protected string _Url = null;

        #region IIssueTracker Members

        public string Url
        {
            get
            {
                return this._Url;
            }
            set
            {
                this._Url = value;
            }
        }

        public virtual List<string> GetUserItems(string UserName)
        {
            return new List<string>();
        }

        public virtual void Init()
        {
            
        }

        #endregion
    }
}
