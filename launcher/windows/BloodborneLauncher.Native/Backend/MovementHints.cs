using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
internal static class MovementHints
{
    public static string Turning => UiText.Locale.Split('-')[0] switch
    {
        "es" => "Controla la rapidez de los cambios de dirección con teclado al caminar, moverse normalmente y esprintar. Las direcciones opuestas responden inmediatamente. 0 desactiva el suavizado.",
        "fr" => "Règle la vitesse des changements de direction au clavier, en marchant, en courant ou en sprintant. Les directions opposées répondent immédiatement. 0 désactive le lissage.",
        "de" => "Bestimmt die Geschwindigkeit von Richtungswechseln mit der Tastatur beim Gehen, Laufen und Sprinten. Gegenrichtungen reagieren sofort. 0 deaktiviert die Glättung.",
        "it" => "Regola la velocità dei cambi di direzione con la tastiera mentre cammini, corri o scatti. Le direzioni opposte rispondono subito. 0 disattiva la transizione graduale.",
        "pt" => "Controla a velocidade das mudanças de direção pelo teclado ao caminhar, correr ou disparar. Direções opostas respondem imediatamente. 0 desativa a suavização.",
        "ru" => "Задаёт скорость смены направления при движении с клавиатуры: шагом, обычным бегом и спринтом. Противоположные направления срабатывают мгновенно. 0 отключает сглаживание.",
        "ja" => "キーボード操作時の方向転換速度を設定します。歩行、通常移動、ダッシュのすべてに適用されます。反対方向への切り替えは即座に反映されます。0で補間を無効にします。",
        _ => "Controls keyboard direction changes while walking, moving normally and sprinting. Opposite directions respond immediately. 0 disables smoothing."
    };
}
