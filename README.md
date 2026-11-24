<p align="center">
  <img src="src/ValoSettings/icon/icon-256.png" width="112" alt="ValoSettings">
</p>

<h1 align="center">ValoSettings</h1>

<p align="center">Enregistre les settings Valorant de tes comptes Riot et copie-les d'un compte à l'autre, sur Windows.</p>

---

## ⚠️ Avertissement — à lire avant d'utiliser

- **Je me désolidarise totalement de tout bannissement, sanction, perte de compte ou autre problème** qui pourrait survenir, que ce soit **sur ton compte Riot / Valorant** ou **sur ton PC** (fichiers, configuration, système), suite à l'utilisation de ce logiciel. **Tu l'utilises entièrement à tes risques et périls.**
- **Je n'encourage pas le smurf, je le condamne.** Ce logiciel n'est pas fait pour faciliter le smurf ou le contournement des règles de Riot Games. Il sert à retrouver tes propres réglages sur tes propres comptes.
- Ce projet n'est **ni affilié, ni approuvé, ni soutenu par Riot Games**. *Valorant* et *Riot Games* sont des marques de Riot Games, Inc.
- L'outil utilise l'API **locale et non officielle** du Riot Client, comme le jeu lui-même, pour lire et écrire les préférences du compte connecté. Riot peut la modifier ou la bloquer à tout moment.
- Le logiciel est fourni **« tel quel », sans aucune garantie**.

---

## Fonctionnalités

- **Comptes** : liste des comptes auxquels tu t'es déjà connecté (avec l'appli ouverte), avec surnoms.
- **Enregistrer** : choisis les settings à garder, par catégorie ou un par un (viseur, sensi, touches, minimap, audio, interface, graphismes).
- **Appliquer** un profil sur le compte connecté, catégorie par catégorie. Un profil partiel ne touche que ce qu'il contient.
- **Exporter / importer** un profil en fichier `.valoprofile` ou en code à partager.
- **Sauvegarde automatique** du compte cible avant chaque application. Elle est **supprimée définitivement au bout d'1 h**.
- Les infos propres au compte (CGU acceptées, tutos vus…) ne sont jamais copiées.

## Télécharger

**[⬇ ValoSettings v1.0.0 (Windows 10/11, 64 bits)](https://github.com/Robert229919/ValoSettings/releases/latest)**. Un seul `.exe`, rien à installer. Il n'est pas signé : si SmartScreen s'affiche, clique sur « Informations complémentaires » puis « Exécuter quand même ».

## Utilisation

1. Ouvre le **Riot Client** et connecte-toi au compte source → **+ Enregistrer**.
2. Connecte-toi au compte cible (**Valorant fermé**, Riot Client ouvert).
3. **Profils** → **Appliquer →** → choisis les catégories → **Appliquer**.

Les profils sont stockés dans `%APPDATA%\ValoSettings\profiles`.

## Compiler

Prérequis : Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), Microsoft Edge WebView2 (inclus dans Windows 11).

```powershell
dotnet publish src\ValoSettings -c Release -o dist
```

L'exécutable est `dist\ValoSettings.exe`.

## Structure

- `src/ValoSettings/*.cs` : application C# (WinForms + WebView2), API locale Riot, profils, catégories.
- `src/ValoSettings/web/` : interface (HTML/CSS/JS), embarquée dans l'exe. Ouverte dans un navigateur via un petit serveur statique, elle affiche des données de démo.
- `src/ValoSettings/icon/` : icône (SVG sources + `make-icon.ps1` pour régénérer `app.ico`).
