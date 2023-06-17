using DevBasics.CarManagement.Dependencies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    internal interface ICarBrand
    {
        public void GenerateRegistration(string endCustomerRegistrationReference, out string registrationId, out string registrationNumber);
    }
}
