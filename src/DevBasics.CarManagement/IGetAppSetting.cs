using DevBasics.CarManagement.Dependencies;
using System.Threading.Tasks;

namespace DevBasics.CarManagement
{
    public interface IGetAppSetting
    {
        Task<AppSettingDto> GetAppSettingAsync(string salesOrgIdentifier, CarBrand requestOrigin);
        
    }
}
