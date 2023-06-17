using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevBasics.CarManagement.Dependencies
{
    internal sealed class LeasingRegistrationRepository : IGetAppSetting, IUpdateCar, IInsertHistory
    {
        public IDictionary<int, Tuple<CarRegistrationDto, string, string, string>> Registrations { get; } = new Dictionary<int, Tuple<CarRegistrationDto, string, string, string>>();

        public Task<AppSettingDto> GetAppSettingAsync(string salesOrgIdentifier, CarBrand requestOrigin)
        {
            return Task.FromResult(new AppSettingDto
            {
                SoldTo = "SoldTo01"
            });
        }

        public Task<int> InsertHistoryAsync(CarRegistrationDto dbCar, string userName, string transactionStateName = null, string transactionTypeName = null)
        {
            if (!Registrations.ContainsKey(dbCar.RegisteredCarId))
            {
                Registrations.Add(dbCar.RegisteredCarId, Tuple.Create(dbCar, userName, transactionStateName, transactionTypeName));

                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }

        public Task<bool> UpdateCarAsync(CarRegistrationDto dbCar)
        {
            if (!Registrations.ContainsKey(dbCar.RegisteredCarId))
            {
                return Task.FromResult(false);
            }

            var current = Registrations[dbCar.RegisteredCarId];

            Registrations[dbCar.RegisteredCarId] = Tuple.Create(dbCar, current.Item2, current.Item3, current.Item4);

            return Task.FromResult(true);
        }
    }
}
