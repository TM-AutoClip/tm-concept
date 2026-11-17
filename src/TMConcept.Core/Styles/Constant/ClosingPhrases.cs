using TMConcept.Core.Profiles;

namespace TMConcept.Core.Styles;

/// <summary>
/// The closing sentences per video language (fixed text, no model writes them): one entry per output language of
/// <see cref="ContentLanguages"/>, each with a line for every <see cref="ClosingPhrase"/> in both wordings. Informal address, as the app
/// speaks to its users. A language without an entry gets no closing (the video step logs a warning; <see cref="VideoStyleRules.Closing"/>).
/// In Russian a channel is "subscribed to" on every platform, so both wordings are the same.
/// </summary>
public static class ClosingPhrases
{
    /// <summary>Every language with closing sentences.</summary>
    public static readonly IReadOnlyList<ClosingLanguage> All =
    [
        new(ContentLanguages.Turkish, "Abone ol", "Takip et",
        [
            new(ClosingPhrase.WhatsNext, "Devamı için abone olmayı unutma!", "Devamı için takip etmeyi unutma!"),
            new(ClosingPhrase.MoreVideos, "Diğer videolar için abone olmayı unutma!", "Diğer videolar için takip etmeyi unutma!"),
            new(ClosingPhrase.Short, "Abone olmayı unutma!", "Takip etmeyi unutma!"),
            new(ClosingPhrase.Thanks, "İzlediğin için teşekkürler, abone olmayı unutma!", "İzlediğin için teşekkürler, takip etmeyi unutma!"),
        ]),
        new(ContentLanguages.English, "Subscribe", "Follow",
        [
            new(ClosingPhrase.WhatsNext, "Don't forget to subscribe to see what's next!", "Don't forget to follow to see what's next!"),
            new(ClosingPhrase.MoreVideos, "Don't forget to subscribe for more videos!", "Don't forget to follow for more videos!"),
            new(ClosingPhrase.Short, "Don't forget to subscribe!", "Don't forget to follow!"),
            new(ClosingPhrase.Thanks, "Thanks for watching, don't forget to subscribe!", "Thanks for watching, don't forget to follow!"),
        ]),
        new(ContentLanguages.German, "Abonnieren", "Folgen",
        [
            new(ClosingPhrase.WhatsNext, "Vergiss nicht zu abonnieren, um zu sehen, wie es weitergeht!",
                "Vergiss nicht zu folgen, um zu sehen, wie es weitergeht!"),
            new(ClosingPhrase.MoreVideos, "Vergiss nicht zu abonnieren, um weitere Videos zu sehen!", "Vergiss nicht zu folgen, um weitere Videos zu sehen!"),
            new(ClosingPhrase.Short, "Vergiss nicht zu abonnieren!", "Vergiss nicht zu folgen!"),
            new(ClosingPhrase.Thanks, "Danke fürs Zuschauen, vergiss nicht zu abonnieren!", "Danke fürs Zuschauen, vergiss nicht zu folgen!"),
        ]),
        new(ContentLanguages.Spanish, "Suscríbete", "Síguenos",
        [
            new(ClosingPhrase.WhatsNext, "¡No olvides suscribirte para ver lo que viene!", "¡No olvides seguirnos para ver lo que viene!"),
            new(ClosingPhrase.MoreVideos, "¡No olvides suscribirte para ver más videos!", "¡No olvides seguirnos para ver más videos!"),
            new(ClosingPhrase.Short, "¡No olvides suscribirte!", "¡No olvides seguirnos!"),
            new(ClosingPhrase.Thanks, "¡Gracias por ver, no olvides suscribirte!", "¡Gracias por ver, no olvides seguirnos!"),
        ]),
        new(ContentLanguages.French, "S'abonner", "Suivre",
        [
            new(ClosingPhrase.WhatsNext, "N'oublie pas de t'abonner pour voir la suite !", "N'oublie pas de nous suivre pour voir la suite !"),
            new(ClosingPhrase.MoreVideos, "N'oublie pas de t'abonner pour voir d'autres vidéos !",
                "N'oublie pas de nous suivre pour voir d'autres vidéos !"),
            new(ClosingPhrase.Short, "N'oublie pas de t'abonner !", "N'oublie pas de nous suivre !"),
            new(ClosingPhrase.Thanks, "Merci d'avoir regardé, n'oublie pas de t'abonner !", "Merci d'avoir regardé, n'oublie pas de nous suivre !"),
        ]),
        new(ContentLanguages.Italian, "Iscriviti", "Seguici",
        [
            new(ClosingPhrase.WhatsNext, "Non dimenticare di iscriverti per vedere il seguito!", "Non dimenticare di seguirci per vedere il seguito!"),
            new(ClosingPhrase.MoreVideos, "Non dimenticare di iscriverti per altri video!", "Non dimenticare di seguirci per altri video!"),
            new(ClosingPhrase.Short, "Non dimenticare di iscriverti!", "Non dimenticare di seguirci!"),
            new(ClosingPhrase.Thanks, "Grazie per la visione, non dimenticare di iscriverti!", "Grazie per la visione, non dimenticare di seguirci!"),
        ]),
        new(ContentLanguages.Portuguese, "Inscreva-se", "Siga",
        [
            new(ClosingPhrase.WhatsNext, "Não esqueça de se inscrever para ver a continuação!", "Não esqueça de nos seguir para ver a continuação!"),
            new(ClosingPhrase.MoreVideos, "Não esqueça de se inscrever para ver mais vídeos!", "Não esqueça de nos seguir para ver mais vídeos!"),
            new(ClosingPhrase.Short, "Não esqueça de se inscrever!", "Não esqueça de nos seguir!"),
            new(ClosingPhrase.Thanks, "Obrigado por assistir, não esqueça de se inscrever!", "Obrigado por assistir, não esqueça de nos seguir!"),
        ]),
        new(ContentLanguages.Dutch, "Abonneren", "Volgen",
        [
            new(ClosingPhrase.WhatsNext, "Vergeet je niet te abonneren om te zien hoe het verdergaat!", "Vergeet ons niet te volgen om te zien hoe het verdergaat!"),
            new(ClosingPhrase.MoreVideos, "Vergeet je niet te abonneren voor meer video's!", "Vergeet ons niet te volgen voor meer video's!"),
            new(ClosingPhrase.Short, "Vergeet je niet te abonneren!", "Vergeet ons niet te volgen!"),
            new(ClosingPhrase.Thanks, "Bedankt voor het kijken, vergeet je niet te abonneren!", "Bedankt voor het kijken, vergeet ons niet te volgen!"),
        ]),
        new(ContentLanguages.Polish, "Subskrybuj", "Obserwuj",
        [
            new(ClosingPhrase.WhatsNext, "Nie zapomnij zasubskrybować, żeby zobaczyć ciąg dalszy!", "Nie zapomnij nas obserwować, żeby zobaczyć ciąg dalszy!"),
            new(ClosingPhrase.MoreVideos, "Nie zapomnij zasubskrybować, żeby zobaczyć więcej filmów!",
                "Nie zapomnij nas obserwować, żeby zobaczyć więcej filmów!"),
            new(ClosingPhrase.Short, "Nie zapomnij zasubskrybować!", "Nie zapomnij nas obserwować!"),
            new(ClosingPhrase.Thanks, "Dzięki za obejrzenie, nie zapomnij zasubskrybować!", "Dzięki za obejrzenie, nie zapomnij nas obserwować!"),
        ]),
        new(ContentLanguages.Russian, "Подписаться", "Подписаться",
        [
            new(ClosingPhrase.WhatsNext, "Не забудь подписаться, чтобы увидеть продолжение!", "Не забудь подписаться, чтобы увидеть продолжение!"),
            new(ClosingPhrase.MoreVideos, "Не забудь подписаться, чтобы не пропустить новые видео!", "Не забудь подписаться, чтобы не пропустить новые видео!"),
            new(ClosingPhrase.Short, "Не забудь подписаться!", "Не забудь подписаться!"),
            new(ClosingPhrase.Thanks, "Спасибо за просмотр, не забудь подписаться!", "Спасибо за просмотр, не забудь подписаться!"),
        ]),
        new(ContentLanguages.Swedish, "Prenumerera", "Följ",
        [
            new(ClosingPhrase.WhatsNext, "Glöm inte att prenumerera för att se fortsättningen!", "Glöm inte att följa oss för att se fortsättningen!"),
            new(ClosingPhrase.MoreVideos, "Glöm inte att prenumerera för fler videor!", "Glöm inte att följa oss för fler videor!"),
            new(ClosingPhrase.Short, "Glöm inte att prenumerera!", "Glöm inte att följa oss!"),
            new(ClosingPhrase.Thanks, "Tack för att du tittade, glöm inte att prenumerera!", "Tack för att du tittade, glöm inte att följa oss!"),
        ]),
        new(ContentLanguages.Indonesian, "Subscribe", "Follow",
        [
            new(ClosingPhrase.WhatsNext, "Jangan lupa subscribe untuk lihat kelanjutannya!", "Jangan lupa follow untuk lihat kelanjutannya!"),
            new(ClosingPhrase.MoreVideos, "Jangan lupa subscribe untuk video lainnya!", "Jangan lupa follow untuk video lainnya!"),
            new(ClosingPhrase.Short, "Jangan lupa subscribe!", "Jangan lupa follow!"),
            new(ClosingPhrase.Thanks, "Terima kasih sudah menonton, jangan lupa subscribe!", "Terima kasih sudah menonton, jangan lupa follow!"),
        ]),
    ];
}
