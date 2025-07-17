[![Create Release](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/release.yml/badge.svg)](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/release.yml)[![Build .NET EXE](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/actions/workflows/dotnet.yml)

# BBR-Ban-Sync - Application de Synchronisation de Bans Optimisée

## 🚀 Nouveautés de la Version Optimisée

Cette version a été complètement refactorisée pour améliorer les performances, la maintenabilité et la fiabilité :

### ✨ Améliorations Principales

- **Architecture Modulaire** : Code organisé en services séparés avec injection de dépendances
- **Performances Optimisées** : Cache intelligent, gestion optimisée des ressources HTTP
- **Gestion d'Erreurs Robuste** : Système de retry automatique et logging détaillé
- **Configuration Moderne** : Migration vers `appsettings.json` avec validation
- **Tests Unitaires** : Couverture de tests pour assurer la qualité
- **Sécurité Renforcée** : Meilleure gestion des secrets et configurations

## 📋 Description

Cette application C# Console synchronise les identifiants Steam (SteamID2) d'une base de données MySQL (Sourcebans++) vers un fichier de sortie txt (SteamID64). Elle surveille également les modifications du fichier de sortie et met à jour la base de données en conséquence. L'application utilise l'API Steam pour convertir les SteamID64 en SteamID2 et récupérer les noms des joueurs.

## 🎯 Fonctionnalités

- ✅ Synchronisation bidirectionnelle (base de données ↔ fichier)
- ✅ Surveillance en temps réel des modifications de fichier
- ✅ Notifications Discord avec embeds personnalisés
- ✅ Cache intelligent pour les données Steam
- ✅ Vérification automatique des mises à jour GitHub
- ✅ Système de retry automatique pour les opérations critiques
- ✅ Logging détaillé avec NLog
- ✅ Configuration flexible et validation
- ✅ Tests unitaires intégrés

## 🏗️ Architecture

```
BBR-Ban-Sync/
├── Interfaces/          # Contrats des services
├── Models/             # Modèles de données et configuration
├── Services/           # Implémentations des services
│   ├── BanSyncService.cs      # Service principal
│   ├── DatabaseService.cs     # Gestion base de données
│   ├── SteamService.cs        # API Steam et conversions
│   ├── DiscordService.cs      # Notifications Discord
│   ├── FileWatcherService.cs  # Surveillance fichiers
│   └── GitHubService.cs       # Vérification mises à jour
├── Tests/              # Tests unitaires
└── Program.cs          # Point d'entrée avec DI
```

## ⚙️ Configuration

### Fichier appsettings.json

L'application utilise maintenant `appsettings.json` pour la configuration :

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=sourcebans;Uid=user;Pwd=password;SslMode=Required;"
  },
  "BanSync": {
    "OutputFile": "C:\\path\\to\\Blacklist.txt",
    "SteamAPIKey": "your-steam-api-key",
    "ServerID": 5,
    "DebugMode": false,
    "SyncIntervalMinutes": 1,
    "ReleaseCheckIntervalHours": 1,
    "FileWatcherEnabled": true,
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 5,
    "CacheExpirationMinutes": 30
  },
  "Discord": {
    "Enabled": true,
    "WebhookUrls": [
      "https://discord.com/api/webhooks/your_webhook_id/your_webhook_token"
    ],
    "EmbedColor": 16711680,
    "ReclamationUrl": "https://www.clan-rmg.com/playerpanel/"
  },
  "GitHub": {
    "Owner": "Royal-Multi-Gamers",
    "Repository": "Ban-Sync-Sourcebans",
    "CurrentVersion": "v0.0.5"
  }
}
```

### Variables d'Environnement

Vous pouvez également utiliser des variables d'environnement pour les configurations sensibles :

```bash
ConnectionStrings__DefaultConnection="Server=localhost;Database=sourcebans;Uid=user;Pwd=password;"
BanSync__SteamAPIKey="your-steam-api-key"
Discord__WebhookUrls__0="https://discord.com/api/webhooks/..."
```

## 🚀 Installation et Déploiement

### Prérequis

- .NET 8.0 Runtime
- MySQL/MariaDB avec Sourcebans++
- Clé API Steam
- Webhooks Discord (optionnel)

### Installation

1. **Téléchargez la dernière version** depuis les [Releases GitHub](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/releases)

2. **Extrayez l'archive** dans le répertoire de votre choix

3. **Configurez l'application** en modifiant `appsettings.json`

4. **Lancez l'application** :
   ```bash
   dotnet BBR-Ban-Sync.dll
   ```

### Compilation depuis les Sources

```bash
git clone https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans.git
cd Ban-Sync-Sourcebans
dotnet restore
dotnet build --configuration Release
dotnet publish --configuration Release --self-contained false
```

## 🧪 Tests

Exécutez les tests unitaires :

```bash
dotnet test BBR-Ban-Sync.Tests/
```

Avec couverture de code :

```bash
dotnet test BBR-Ban-Sync.Tests/ --collect:"XPlat Code Coverage"
```

## 📊 Monitoring et Logs

### Logs

Les logs sont générés dans le dossier `logs/` avec rotation quotidienne :

- `logs/YYYY-MM-DD.log` : Logs quotidiens
- Console : Affichage en temps réel

### Métriques

L'application log automatiquement :
- Nombre de bans synchronisés
- Temps de réponse des APIs
- Erreurs et tentatives de retry
- Statut des connexions

## 🔧 Développement

### Structure du Code

- **Services** : Logique métier séparée par responsabilité
- **Interfaces** : Contrats pour faciliter les tests et l'extensibilité
- **Models** : Objets de données et configuration
- **Tests** : Tests unitaires avec Moq et FluentAssertions

### Ajout de Nouvelles Fonctionnalités

1. Créez l'interface dans `Interfaces/`
2. Implémentez le service dans `Services/`
3. Ajoutez les tests dans `Tests/`
4. Enregistrez le service dans `Program.cs`

## 🐛 Dépannage

### Problèmes Courants

**Erreur de connexion à la base de données :**
```
Vérifiez la chaîne de connexion dans appsettings.json
Assurez-vous que MySQL est accessible
```

**Fichier de sortie inaccessible :**
```
Vérifiez les permissions du répertoire
Assurez-vous que le chemin existe
```

**API Steam rate limited :**
```
L'application gère automatiquement les limites
Vérifiez que votre clé API est valide
```

### Logs de Debug

Activez le mode debug dans la configuration :
```json
{
  "BanSync": {
    "DebugMode": true
  }
}
```

## 🤝 Contribution

Les contributions sont les bienvenues ! Veuillez :

1. Fork le projet
2. Créer une branche pour votre fonctionnalité
3. Ajouter des tests pour votre code
4. Soumettre une Pull Request

## 📄 Licence

Ce projet est sous licence MIT. Voir le fichier [LICENSE](LICENSE) pour plus de détails.

## 🏢 Royal Multi Gamers Association

**Site Web :** https://www.clan-rmg.com/

---

## 📈 Changelog

### v0.0.5 (Version Optimisée)
- ✨ Refactorisation complète avec architecture modulaire
- ✨ Injection de dépendances avec Microsoft.Extensions
- ✨ Configuration moderne avec appsettings.json
- ✨ Cache intelligent pour les données Steam
- ✨ Système de retry automatique
- ✨ Tests unitaires intégrés
- ✨ Amélioration des performances et de la stabilité
- ✨ Logging détaillé et monitoring
- ✨ Validation de configuration
- ✨ Gestion d'erreurs robuste

### v0.0.4 (Version Précédente)
- Fonctionnalités de base de synchronisation
- Notifications Discord
- Surveillance de fichier
- Vérification des mises à jour GitHub
