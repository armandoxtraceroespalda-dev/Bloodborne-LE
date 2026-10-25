using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
internal static class ControlResetText
{
    public static (string Title, string Body, string Tooltip) For(bool all, string device)
    {
        bool keyboard = device == "keyboard_mouse";
        return UiText.Locale.Split('-')[0] switch
        {
            "es" => (all ? "¿Restablecer todos los controles?" : "¿Restablecer las asignaciones?",
                keyboard ? "Se restaurarán los valores predeterminados de teclado y ratón." : "Se restaurarán los valores predeterminados del mando.",
                keyboard ? "Se conservará la configuración del mando." : "Se conservará la configuración de teclado y ratón."),
            "fr" => (all ? "Réinitialiser toutes les commandes ?" : "Réinitialiser les affectations ?",
                keyboard ? "Les valeurs par défaut du clavier et de la souris seront restaurées." : "Les valeurs par défaut de la manette seront restaurées.",
                keyboard ? "Les réglages de la manette seront conservés." : "Les réglages du clavier et de la souris seront conservés."),
            "de" => (all ? "Alle Steuerungseinstellungen zurücksetzen?" : "Tastenbelegung zurücksetzen?",
                keyboard ? "Die Standardwerte für Tastatur und Maus werden wiederhergestellt." : "Die Standardwerte für das Gamepad werden wiederhergestellt.",
                keyboard ? "Die Gamepad-Einstellungen bleiben erhalten." : "Die Einstellungen für Tastatur und Maus bleiben erhalten."),
            "it" => (all ? "Ripristinare tutti i controlli?" : "Ripristinare le assegnazioni?",
                keyboard ? "Verranno ripristinati i valori predefiniti di tastiera e mouse." : "Verranno ripristinati i valori predefiniti del gamepad.",
                keyboard ? "Le impostazioni del gamepad saranno conservate." : "Le impostazioni di tastiera e mouse saranno conservate."),
            "pt" => (all ? "Restaurar todos os controles?" : "Restaurar os atalhos?",
                keyboard ? "Os padrões de teclado e mouse serão restaurados." : "Os padrões do controle serão restaurados.",
                keyboard ? "As configurações do controle serão mantidas." : "As configurações de teclado e mouse serão mantidas."),
            "ru" => (all ? "Сбросить все настройки управления?" : "Сбросить назначения кнопок?",
                keyboard ? "Будут восстановлены настройки клавиатуры и мыши по умолчанию." : "Будут восстановлены настройки геймпада по умолчанию.",
                keyboard ? "Настройки геймпада сохранятся." : "Настройки клавиатуры и мыши сохранятся."),
            "ja" => (all ? "操作設定をすべて初期化しますか？" : "キー割り当てを初期化しますか？",
                keyboard ? "キーボードとマウスの設定を初期値に戻します。" : "ゲームパッドの設定を初期値に戻します。",
                keyboard ? "ゲームパッドの設定は保持されます。" : "キーボードとマウスの設定は保持されます。"),
            _ => (all ? "Reset all control settings?" : "Reset keybindings?",
                keyboard ? "Keyboard and mouse defaults will be restored." : "Gamepad defaults will be restored.",
                keyboard ? "Gamepad settings will be kept." : "Keyboard and mouse settings will be kept.")
        };
    }
}
