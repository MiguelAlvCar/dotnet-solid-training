using DevBasics.CarManagement.Dependencies;
using System;

namespace DevBasics.CarManagement
{
    public static class IdGenerator
    {
        public static string GenerateId()
        {
            return DateTime.Now.Ticks.ToString();
        }
    }
}