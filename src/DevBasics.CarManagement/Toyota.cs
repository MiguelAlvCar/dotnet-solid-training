using DevBasics.CarManagement.Dependencies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    internal class Toyota : ICarBrand
    {
        public void GenerateRegistration(string endCustomerRegistrationReference, out string registrationId, out string registrationNumber)
        {
            registrationId = IdGenerator.GenerateId();

            if (string.IsNullOrWhiteSpace(endCustomerRegistrationReference))
            {
                registrationNumber = registrationId;
                return;
            }
            registrationNumber = FormatRegistrationReference(endCustomerRegistrationReference, 32);
        }
        private static string FormatRegistrationReference(string endCustomerRegistrationReference, int maxLength)
        {
            string endCustomerRegistrationReferenceShort = endCustomerRegistrationReference.Length > 23
                            ? endCustomerRegistrationReference.Substring(0, 23)
                            : endCustomerRegistrationReference;

            Guid uniqueValue = Guid.NewGuid();
            string uniqueValueBase64 = Convert.ToBase64String(uniqueValue.ToByteArray());

            // Remove unnecessary characters from base64 string.
            uniqueValueBase64 = uniqueValueBase64.Replace("=", string.Empty);
            uniqueValueBase64 = uniqueValueBase64.Replace("+", string.Empty);
            uniqueValueBase64 = uniqueValueBase64.Replace("/", string.Empty);
            string uniqueValueBase64Short = uniqueValueBase64.Substring(0, 8);

            string depRegistrationNumber = $"{endCustomerRegistrationReferenceShort}-{uniqueValueBase64Short}";

            return depRegistrationNumber.Length > maxLength ? depRegistrationNumber.Substring(0, maxLength) : depRegistrationNumber;
        }
    }
}
