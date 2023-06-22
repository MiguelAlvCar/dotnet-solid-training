using AutoMapper;
using DevBasics.CarManagement.Dependencies;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevBasics.CarManagement
{
    internal sealed class Program
    {
        internal static async Task Main()
        {
            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddTransient<IUpdateCar, LeasingRegistrationRepository>();
                    services.AddTransient<IBulkRegistrationService, BulkRegistrationServiceMock>();
                    services.AddTransient<IMapper, IMapper>(sp => {
                        var model = new CarRegistrationModel();
                        var configuration = new MapperConfiguration(cnfgrtn => model.CreateMappings(cnfgrtn));
                        return configuration.CreateMapper();
                    });
                    services.AddTransient<ICarRegistrationRepository, CarRegistrationRepository>();
                })
                .Build();

            using IServiceScope serviceScope = host.Services.CreateScope();
            IServiceProvider provider = serviceScope.ServiceProvider;
            ICarRegistrationRepository carRegistrationRepository = provider.GetRequiredService<ICarRegistrationRepository>();

            var bulkRegistrationServiceMock = new BulkRegistrationServiceMock();
            var leasingRegistrationRepository = new LeasingRegistrationRepository();


            var model = new CarRegistrationModel();
            var configuration = new MapperConfiguration(cnfgrtn => model.CreateMappings(cnfgrtn));
            var mapper = configuration.CreateMapper();

            var service = new CarRegistrator(
                new CarManagementService(
                    mapper,
                    new CarManagementSettings(),
                    new HttpHeaderSettings(),
                    bulkRegistrationServiceMock,
                    carRegistrationRepository,
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
