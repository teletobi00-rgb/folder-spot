using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Explorer.App.Services;

/// <summary>
/// 설치된 Outlook으로 선택 파일을 첨부한 '새 메일 작성' 창을 연다. Office 참조 없이 late-bound COM(dynamic)으로
/// 호출하므로 빌드 의존성이 없고, Outlook이 없으면 false를 돌려준다. STA(UI) 스레드에서 호출해야 한다.
/// </summary>
public static class OutlookMailService
{
    public static bool NewMailWithAttachments(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var type = Type.GetTypeFromProgID("Outlook.Application");
        if (type is null)
        {
            return false; // Outlook 미설치.
        }

        try
        {
            dynamic? app = Activator.CreateInstance(type);
            if (app is null)
            {
                return false;
            }

            dynamic mail = app.CreateItem(0); // olMailItem
            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    mail.Attachments.Add(path);
                }
            }

            mail.Display(false); // 모달 아님 — 작성 창만 표시.
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException
            or MissingMemberException or TargetInvocationException or AmbiguousMatchException)
        {
            return false;
        }
    }
}
