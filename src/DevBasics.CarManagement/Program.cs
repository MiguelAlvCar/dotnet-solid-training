using AutoMapper;
using DevBasics.CarManagement.Dependencies;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    internal sealed class Program
    {
        internal static async Task Main()
        {
            var model = new CarRegistrationModel();
            var configuration = new MapperConfiguration(cnfgrtn => model.CreateMappings(cnfgrtn));
            var mapper = configuration.CreateMapper();

            var bulkRegistrationServiceMock = new BulkRegistrationServiceMock();
            var leasingRegistrationRepository = new LeasingRegistrationRepository();
            var carRegistrationRepositoryMock = new CarRegistrationRepository(
                leasingRegistrationRepository,
                bulkRegistrationServiceMock,
                mapper);

            var service = new CarRegistrationService(
                new CarManagementService(
                    mapper,
                    new CarManagementSettings(),
                    new HttpHeaderSettings(),
                    bulkRegistrationServiceMock,
                    carRegistrationRepositoryMock,
                    leasingRegistrationRepository,
                    leasingRegistrationRepository,
                    leasingRegistrationRepository),
                new CarRegistrationRepository(
                    leasingRegistrationRepository,
                    bulkRegistrationServiceMock,
                    mapper)
                );



            var result = await service.RegisterCarsAsync(
                new RegisterCarsModel
                {
                    CompanyId = "Company",
                    CustomerId = "Customer",
                    VendorId = "Vendor",
                    Cars = new List<CarRegistrationModel>
                    {
                        new CarRegistrationModel
                        {
                            CompanyId = "Company",
                            CustomerId = "Customer",
                            VehicleIdentificationNumber = Guid.NewGuid().ToString(),
                            DeliveryDate = DateTime.Now.AddDays(14).Date,
                            ErpDeliveryNumber = Guid.NewGuid().ToString()
                        }
                    }
                },
                false,
                new Claims());
        }
    }
}
