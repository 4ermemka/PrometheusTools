using System;

namespace ParamsSynchronizer
{
    internal interface ITrackable
    {
        Action<string> OnChangeDetected { get; set; }

        void Update(string data);
    }
}