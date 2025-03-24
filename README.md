[![Build .NET EXE](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/dotnet.yml)
# Application de Synchronisation de Bans depuis un fichier TXT vers Sourcebans et synchronise le Sourcebans vers un fichier TXT

## Description

Cette application synchronise les identifiants Steam (SteamID) d'une base de données MySQL (Sourcebans++) vers un fichier de sortie txt. Elle surveille également les modifications du fichier de sortie et met à jour la base de données en conséquence. L'application utilise l'API Steam pour convertir les SteamID64 en SteamID2 et récupérer les noms des joueurs.

## Fonctionnalités

- Synchronisation initiale de la base de données vers le fichier de sortie.
- Surveillance des modifications du fichier de sortie et mise à jour de la base de données.
- Conversion des SteamID64 en SteamID2.
- Récupération des noms des joueurs via l'API Steam.
- Journalisation des événements et des erreurs avec NLog.

## Configuration

### Fichier de Configuration

L'application utilise un fichier `config.json` pour la configuration. Si ce fichier n'existe pas, un fichier de configuration par défaut sera créé. Voici un exemple de `config.json` :
```
{
  "ConnectionString": {
    "Server": "localhost",
    "Uid": "databaseuser",
    "Pwd": "userpassword",
    "Database": "databasename"
  },
  "OutputFile": "C:\\testps\\Blacklist.txt",
  "SteamAPIKey": "steamapikey",
  "ServerID": 5,
  "DebugMode": true,
  "DiscordWebhook": {
    "Enabled": true,
    "Urls": [
      "https://discord.com/api/webhooks/your_webhook_id/your_webhook_token",
      "https://discord.com/api/webhooks/your_webhook_id/your_webhook_token"
    ]
  }
}
```


### Variables de Configuration

- `ConnectionString`: Informations de connexion à la base de données MySQL.
  - `Server`: Adresse du serveur MySQL.
  - `Uid`: Nom d'utilisateur de la base de données.
  - `Pwd`: Mot de passe de l'utilisateur de la base de données.
  - `Database`: Nom de la base de données.
- `OutputFile`: Chemin du fichier de sortie à surveiller.
- `SteamAPIKey`: Clé API Steam pour accéder aux informations des joueurs.
- `ServerID`: Identifiant du serveur.
- `DebugMode`: Mode de débogage (true/false).

## Déploiement

L'application utilise GitHub Actions pour le déploiement. Le workflow de déploiement se trouve dans `.github/workflows/dotnet.yml`. Voici les étapes principales :

1. Récupération du dépôt.
2. Configuration de .NET.
3. Restauration des dépendances.
4. Compilation de l'application.
5. Exécution des tests.
6. Publication de l'application.
7. Création des artefacts.

## Exécution

Pour exécuter l'application, utilisez la commande suivante :

```
dotnet run
```

Assurez-vous que le fichier `config.json` est correctement configuré avant de lancer l'application.

## Journalisation

L'application utilise NLog pour la journalisation. Le fichier de configuration NLog (`NLog.config`) doit être présent dans le répertoire de l'application. Les journaux sont enregistrés dans le répertoire `logs`.

## Contribuer

Les contributions sont les bienvenues ! Veuillez soumettre des pull requests et signaler les problèmes via GitHub.

## Licence

Ce projet est open source et disponible sous licence MIT.
