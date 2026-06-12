namespace Explorer.Core.Settings;

public interface ISettingsService
{
    /// <summary>
    /// 현재 설정 스냅샷. 스냅샷 자체는 불변이지만 이 참조는 <see cref="Update"/> 호출 시 교체된다.
    /// 읽은 값 기반으로 변경해야 한다면 Current를 직접 읽지 말고 <see cref="Update"/>의 transform 안에서 처리한다.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>디스크에서 설정을 읽는다. 파일이 없거나 손상이면 기본값으로 대체한다.</summary>
    void Load();

    /// <summary>설정을 변환해 저장하고 새 스냅샷을 반환한다.</summary>
    AppSettings Update(Func<AppSettings, AppSettings> transform);
}
