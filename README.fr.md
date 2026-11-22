# Music Widget — Lecteur de musique pour Windows

**Un lecteur de musique gratuit et open source pour Windows, avec un mini-lecteur toujours au premier plan.** Écoute tes fichiers MP3, FLAC, WAV et M4A hors ligne et organise-les en playlists. Le mode Minimaliste tient sur une ligne. Le mode Compact est redimensionnable et affiche un visualiseur audio. Les deux modes proposent des couleurs Liquid Glass personnalisables.

[**Versions Windows**](https://github.com/Ryzen446/music-widget/releases/latest) · [Voir la galerie](README.md#screenshots) · [English](README.md)

![Music Widget pour Windows avec un mini-lecteur rose et un lecteur Compact bleu doté d’un visualiseur audio](docs/screenshots/hero.webp)

## Démarrage rapide

Télécharge et ouvre **[MusicWidget-Setup.exe](https://github.com/Ryzen446/music-widget/releases/latest/download/MusicWidget-Setup.exe)**. Il vérifie .NET Framework 4.8 et ajoute **Music Widget** au menu Démarrer pour ton compte. L’option d’écoute et de téléchargement depuis YouTube est cochée par défaut. Décoche-la si tu veux seulement écouter tes fichiers locaux.

Pour la version native portable, décompresse `MusicWidget-Native-Portable.zip` dans un dossier où tu peux écrire, puis ouvre `MusicWidget.exe`. Windows 11 applique l’acrylique système quand la transparence est disponible. [Acrylique Windows](https://learn.microsoft.com/fr-fr/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)

Pour importer des fichiers audio, ouvre **⋯ → Ajouter de la musique → Lien ou fichier**.

Appuie sur **Ctrl+Maj+M** pour afficher ou masquer le lecteur. Le menu de l’icône près de l’horloge permet aussi de le rouvrir, d’ouvrir le dossier de musique, de **Démarrer avec Windows** et de quitter.

## Fonctionnalités

- **Deux dispositions.** Le mode Minimaliste tient sur une ligne et propose six positions à l’écran. Le mode Compact ajoute un visualiseur et une fenêtre déplaçable et redimensionnable.
- **Une aura à ton goût.** Utilise une couleur prédéfinie, une couleur personnalisée ou un cycle RGB avec des fondus doux. Les menus suivent la teinte. Un réglage réduit les animations.
- **Des playlists locales.** Recherche tes titres, ajoute-les par lot ou réorganise une playlist à la souris ou au clavier. Tu peux parcourir une playlist pendant que ton morceau continue.
- **Des commandes accessibles.** Configure des raccourcis globaux pour afficher ou masquer le lecteur, mettre en pause, changer de titre, régler le volume ou quitter. Le lecteur propose aussi une minuterie, la vitesse de lecture et le premier plan.
- **Recherche et écoute intégrées.** Avec les outils en ligne facultatifs, découvre des résultats YouTube avec miniatures, écoute avant d’enregistrer et choisis la bibliothèque ou une playlist. Le panneau affiche la progression en octets lorsque la taille totale est disponible.

## Outils en ligne facultatifs

Dans une version qui propose la configuration des outils en ligne, ouvre **⋯ → Ajouter de la musique** puis **Activer les outils en ligne**. Le lecteur télécharge les copies manquantes de yt-dlp, Deno, FFmpeg et ffprobe, puis vérifie leurs sommes SHA-256. Il utilise `%LOCALAPPDATA%\MusicWidget\Data\bin` pour la version installée ou `bin` à côté de la version portable. La lecture et les playlists locales sont disponibles dès le premier lancement.

Tu peux aussi placer tes propres copies dans le dossier `bin` correspondant. [Versions de yt-dlp](https://github.com/yt-dlp/yt-dlp/releases/latest) · [Configuration du runtime JavaScript](https://github.com/yt-dlp/yt-dlp/wiki/EJS) · [Téléchargement de FFmpeg](https://ffmpeg.org/download.html)

**Rechercher** interroge les résultats publics de YouTube. **Lien ou fichier** accepte un fichier local, une URL YouTube ou le lien d’un titre Spotify. Pour Spotify, les métadonnées publiques servent à chercher un résultat correspondant sur YouTube. Télécharge les contenus que tu as l’autorisation d’enregistrer.

## Données locales et lecture en arrière-plan

Le widget fonctionne sans compte ni clé API personnelle. La version installée conserve les fichiers, playlists et réglages dans `%LOCALAPPDATA%\MusicWidget\Data`. Ce dossier est conservé lors de la désinstallation. La version portable conserve ses données dans le dossier décompressé. La recherche et l’écoute en ligne contactent les services concernés et leurs serveurs d’image ou d’audio.

Si tu utilisais une ancienne version portable, tu peux copier les fichiers `playlists.json`, `settings.json` et le dossier `musique` dans le dossier de données de l’application installée, après avoir fermé Music Widget. Si une bibliothèque existe déjà à cet emplacement, garde une copie avant de remplacer des fichiers.

Quand le widget est masqué, l’audio continue. Les animations du titre, le cycle RGB et le visualiseur se mettent en pause quand leurs éléments sont inactifs. Le réglage automatique suit la langue de Windows si elle est prise en charge et utilise l’anglais dans les autres cas.

## Licence et crédits

Music Widget est distribué sous [licence MIT](LICENSE). Les contrôles WPF natifs reprennent les styles Liquid Glass de la version précédente, basée sur Open Glass UI. Le dépôt conserve les sources et les notices du rendu précédent. [Open Glass UI](https://github.com/moekoelueker/open-glass-ui) · [Licences de l’interface](frontend/licenses) · [Mentions WebView2](lib/webview2)

[Compilation depuis les sources](README.md#build-from-source) · [Galerie complète](README.md#screenshots)
