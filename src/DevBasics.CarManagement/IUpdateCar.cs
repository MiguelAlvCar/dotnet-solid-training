using DevBasics.CarManagement.Dependencies;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    public interface IUpdateCar
    {
        Task<bool> UpdateCarAsync(CarRegistrationDto dbCar);
    }
}
