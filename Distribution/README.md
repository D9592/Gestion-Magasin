# GestionMagasin — Gestion de magasin

## Référence du projet

Le document prioritaire est **Cahier des charges Gestion Magasin.pdf**, version 1.0 du 2 novembre 2026. Il prévaut sur les prompts et choix d'implémentation en cas de contradiction. Le prompt 2 est appliqué dans le présent état du code. Dans son écart concernant le prix d'achat, le PDF V1 ne demandant que le prix de vente, le modèle et l'écran n'ajoutent aucun prix d'achat.

La V1 cible un magasin et un poste par installation, hors ligne, interface en français, FCFA XAF par défaut, produits à l'unité et paiement intégral. Les rôles autorisés sont Administrateur et Vendeur. Les remises, crédits, paiements partiels, taxes automatisées, lots, péremption, produits au poids et multi-magasins restent hors V1.

## Architecture

Solution Visual Studio `StoreManager.slnx`, .NET 10 LTS, WinForms et `Microsoft.Data.Sqlite`. Couches séparées par responsabilité dans `StoreManager.App` :

- `Forms` et `Controls` : présentation WinForms et composants réutilisables.
- `Services` : authentification, règles de permissions, catalogue, stocks, ventes et rapports.
- `Data` : connexions SQLite, repositories paramétrés, scripts de migration.
- `Models` : sessions, catalogue, mouvements, ventes et modèles de rapport.
- `Validators`, `Helpers`, `Resources` : validation, gestion des erreurs et ressources graphiques.
- `StoreManager.Tests` : tests automatisés des services et de l'intégrité SQLite.

L'application stocke la base dans `%LOCALAPPDATA%\GestionMagasin\magasin.sqlite`, hors du répertoire programme, active les clés étrangères, vérifie son intégrité et applique les migrations en transaction. La base de l'ancienne version située dans `%LOCALAPPDATA%\NOVA\GestionMagasin\magasin.sqlite` est copiée de manière cohérente vers le nouvel emplacement uniquement si la nouvelle base n'existe pas; l'ancienne copie n'est jamais supprimée. Si un autre fichier de base `gestion-magasin.db` avec un schéma incompatible est déjà présent dans le même dossier, l'application l'avertit à l'ouverture et le conserve sans le convertir. Un mutex empêche le lancement simultané de plusieurs instances normales.

## Fonctionnalités opérationnelles de cette étape

- Au premier lancement, formulaire de création du premier administrateur; aucun identifiant ou mot de passe par défaut.
- Connexion SQLite, session utilisateur, déconnexion et journalisation des connexions réussies/échouées.
- Mots de passe PBKDF2-HMAC-SHA256, sel aléatoire par compte de 16 octets, dérivation de 32 octets et 310 000 itérations. Les paramètres sont enregistrés avec le hash. Une limitation progressive s'applique après cinq échecs.
- Comptes : création, édition, rôle, activation/désactivation, recherche et réinitialisation du mot de passe. Le dernier administrateur actif ne peut pas être désactivé/rétrogradé; un utilisateur ne peut pas se rétrograder lui-même pendant sa session.
- Catégories : création, recherche, édition, activation/désactivation, unicité accentuée normalisée; aucune suppression physique.
- Produits : création, recherche par référence/désignation, filtres de catégorie, état actif/archivé et disponibilité, édition/archivage, références uniques conservées après archivage, catégorie facultative, prix de vente strictement positif et seuil entier non négatif. Le stock n'est jamais éditable dans la fiche; la liste paginée le lit de `VueStockCourant` et affiche quantité/statut.
- Les contrôles d'accès sont appliqués par les services, en plus de la navigation adaptée au rôle. Les requêtes SQLite sont paramétrées; les opérations d'administration journalisent leurs actions sans secret.

Les maquettes validées ont été conservées comme base visuelle; Nouvelle vente et Gestion des stocks ouvrent désormais les écrans opérationnels. L'historique des ventes et le détail/ticket sont également accessibles depuis la navigation. Le menu latéral regroupe les fonctions d'administration sous une entrée dédiée (utilisateurs et catégories).

## Tableau de bord, rapports et exports

- Le tableau de bord remplace les exemples graphiques par les données réelles : montant, nombre de ventes et unités du jour; ruptures et stocks faibles; tendance sur sept jours; six meilleurs produits sur la période; répartition des paiements du jour. Les métriques de vente respectent le rôle; les alertes excluent les produits archivés. Chaque carte ouvre la liste correspondante avec période ou statut préfiltré.
- Les graphiques sont des contrôles WinForms dessinés localement et n'ajoutent aucune bibliothèque tierce. Les périodes sans données affichent un message vide explicite.
- Le module Rapports, réservé à l'administrateur dans l'interface et les services, propose le détail des ventes (avec statuts), l'agrégat produits vendus par période et l'état courant du stock. Les dates métier de début et de fin sont inclusives; les ventes annulées restent visibles dans le rapport ventes mais sont exclues des montants et quantités agrégées.
- Les listes de rapports sont paginées par 200 lignes et chargées en arrière-plan. Les exports CSV de ventes et stocks reprennent les filtres de recherche, période, vendeur, statut et stock; ils utilisent le séparateur `;`, des champs protégés contre les formules, l'UTF-8 avec BOM et une boîte d'enregistrement.

## Modèle SQLite

`StoreManager.App/Data/Migrations/001_InitialSchema.sql` contient les dix tables demandées par le PDF : `Utilisateur`, `Categorie`, `Produit`, `Vente`, `LigneVente`, `AnnulationVente`, `MouvementStock`, `JournalAudit`, `Parametre` et `VersionSchema`, plus `VueStockCourant` et ses index/contraintes.

Le schéma inclut les hash/sel/paramètres PBKDF2, les rôles, snapshots de lignes, jeton de validation unique, date UTC et date métier, paiement, mouvements signés, correction d'inventaire et unicité des sorties/compensations. Les catégories sont facultatives pour les produits. Les produits sont archivés, pas supprimés. Le prix d'achat n'est pas utilisé en V1, conformément au PDF.

La précédente étape n'avait pas de démarrage de base ni de fichier SQLite livré; son script de schéma n'avait donc pas été appliqué à des données utilisateur. La migration initiale a été alignée avant la première utilisation effective de la base. `002_StockSalesIntegrity.sql` ajoute, sans suppression de données, les règles d'immuabilité de l'historique, de cohérence des origines et paiements, d'interdiction du stock négatif et les index d'accès. Le runner conserve les versions appliquées et toute nouvelle évolution doit ajouter une migration ultérieure sans effacer de données.

## Stocks et ventes de cette étape

- Le solde est la somme des mouvements signés. Les services administrateur enregistrent l'initialisation (une seule fois, y compris un solde initial nul dans l'audit), les réapprovisionnements positifs et les corrections à partir du comptage. Les mouvements et lignes de vente sont immuables en base; le stock négatif est rejeté par le service et par trigger SQLite.
- L'écran stock propose recherche, filtres catégorie/statut, pagination, états colorés, opérations de stock selon le rôle et historique des mouvements.
- Le panier accepte des quantités entières et reprend le prix du catalogue. Une transaction SQLite immédiate recharge prix et stocks; toute variation de prix impose un nouveau contrôle explicite. Le jeton unique protège contre les validations répétées.
- Espèces calcule la monnaie; Mobile Money et carte imposent le règlement exact. Les montants sont conservés dans `Vente`, selon le modèle prioritaire du PDF, sans table `Paiements` séparée.
- L'historique est filtrable. Les vendeurs consultent leurs ventes; seuls les administrateurs peuvent annuler avec motif. L'annulation conserve la vente et crée les mouvements inverses dans la même transaction.
- Le détail de vente ouvre aperçu, impression et réimpression du ticket. Les erreurs d'impression ne modifient pas la vente. Les tests contrôlent le contenu du ticket et l'ouverture de son formulaire; l'impression physique dépend d'une imprimante disponible.

## Sécurité, audit, sauvegarde et restauration

- PBKDF2-HMAC-SHA256 conserve un sel aléatoire distinct et ses paramètres; aucun mot de passe n'est écrit dans `JournalAudit`. Les connexions en échec, y compris durant une période de limitation, sont journalisées sans mot de passe. La fermeture de session crée un événement de déconnexion.
- Les créations et modifications des utilisateurs, catégories et produits journalisent leur contexte et, pour les modifications, les états avant/après. Les mouvements, ventes et annulations sont déjà enregistrés dans leurs transactions respectives et dans l'audit.
- La migration `003_AuditImmutability.sql` ajoute les index de consultation et des déclencheurs qui rejettent toute modification ou suppression des lignes d'audit. `AuditService` protège la consultation par rôle; le formulaire dédié est disponible dans Administration.
- Les sauvegardes s'appuient sur `Microsoft.Data.Sqlite` `BackupDatabase`, sont écrites d'abord dans un fichier temporaire, puis vérifiées (`quick_check`, clés étrangères, version du schéma et cohérence stock/vente/paiement/annulation) avant publication. Une sauvegarde manuelle peut viser un emplacement externe.
- Une sauvegarde automatique est tentée au premier démarrage du jour métier. Seules les sept sauvegardes automatiques les plus récentes sont retenues; les copies manuelles et de sécurité ne sont pas élaguées. Une défaillance ne met pas à jour la date de succès et ne supprime pas de copie précédente.
- La restauration, réservée au rôle administrateur dans le service et l'interface, exige une confirmation, valide le fichier, crée une copie de sécurité, checkpoint le WAL, remplace la base de façon réversible et vérifie la base active. Si le remplacement échoue, le fichier de base précédent est remis en place. Après réussite, l'application retourne à l'écran de connexion.
- Le répertoire de données par défaut est sous `%LocalAppData%`; l'application ajoute une autorisation Windows de contrôle total pour le SID de l'utilisateur courant. Les ACL héritées du répertoire parent restent actives.
- La gestion globale des exceptions affiche un message générique plutôt que le détail SQL ou la trace technique.

## Installation et mise à jour

- Utiliser `Distribution\GestionMagasin_Setup.exe` sur Windows x64. L'assistant en français installe l'application dans Program Files et crée les raccourcis Démarrer et Bureau.
- La première ouverture demande la création d'un compte administrateur; aucun identifiant par défaut n'est distribué.
- Les données restent dans `%LOCALAPPDATA%\GestionMagasin\`, indépendamment du dossier d'installation. La désinstallation conserve ce dossier.
- Une mise à jour ferme l'application, demande à l'ancienne version de créer et vérifier une sauvegarde avant remplacement, installe les binaires, puis applique les migrations et contrôle SQLite au prochain démarrage. En cas d'échec de sauvegarde, l'installation s'arrête.
- Les PDF d'installation, déploiement, utilisation et recette sont livrés dans `Distribution` et copiés dans la documentation de l'installation.

## Dépendances

- `Microsoft.Data.Sqlite` 10.0.14 (aucun autre moteur SQL ni ORM).
- `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3 et `xunit.runner.visualstudio` 3.1.4 pour les tests.
- Dépendances open source, sans bibliothèque payante introduite.
- Licences des composants : Microsoft.Data.Sqlite 10.0.14 (MIT); SQLitePCLRaw.bundle_e_sqlite3, core, lib.e_sqlite3 et provider.e_sqlite3 2.1.11 (Apache-2.0). Les outils de test ne sont pas inclus dans l'exécutable distribué. Le runtime .NET autonome est fourni selon les conditions de licence .NET de Microsoft.

## Publication et licence du logiciel

La publication autonome x64 est produite avec `dotnet publish` en Release pour `win-x64`, auto-hébergée et en fichier unique. L'installateur Inno Setup source le binaire de `Distribution`. La licence du projet est MIT : elle autorise l'utilisation, la copie, la modification, la fusion, la publication, la distribution, la sous-licence et la vente, sous réserve de conserver l'avis de copyright et le texte de permission; le logiciel est fourni sans garantie. Voir [LICENSE](LICENSE).

## Développement

Prérequis : Visual Studio 2022 ou `dotnet` SDK 10, Windows x64 pour WinForms et Inno Setup 7 pour reconstruire le setup. Ouvrir `StoreManager.slnx`; `dotnet restore`, `dotnet build` et `dotnet test` reconstruisent et valident la solution. Les tests créent des bases isolées dans le dossier temporaire. Les scripts SQL versionnés sous `StoreManager.App/Data/Migrations` sont embarqués dans l'application; ajouter une migration numérotée au lieu de modifier un schéma déjà appliqué.

Pour reconstruire la distribution, publier l'application puis compiler `Installer\GestionMagasin.iss` avec Inno Setup Compiler. Ne jamais copier une base de production dans le dossier de distribution. Le code source et les scripts restent disponibles dans le projet, séparément du binaire livré.

La restauration utilise les packages disponibles localement. L'avertissement NuGet `NU1900` indique que l'endpoint d'audit de vulnérabilités n'est pas joignable dans cet environnement (échec TLS/identifiants); le build lui-même reste propre.

## Vérification des étapes précédentes et de cette étape

- `dotnet build StoreManager.slnx --no-restore` : réussi, 0 erreur de compilation et 0 avertissement C#; seulement NU1900 côté audit NuGet.
- `dotnet build StoreManager.slnx --no-restore` : réussi après l'étape 5, 0 erreur de compilation. L'avertissement `NU1900` persiste uniquement car le flux réseau de vérification des vulnérabilités NuGet est indisponible.
- `dotnet build StoreManager.slnx -c Release --no-restore` : réussi, aucune erreur de compilation; `NU1900` signale seulement que le service d'audit des vulnérabilités NuGet est injoignable.
- `dotnet test StoreManager.slnx -c Release --no-restore` : 30 tests réussis, aucun échec, couvrant PBKDF2, connexions, rôles, catalogue, stocks, ventes, paiements, annulations, rapports, CSV, audit, sauvegarde/restauration, erreur SQLite et formulaires WinForms STA.
- La matrice de réception T01-T18 figure dans `Distribution/PV_Recette_T01-T18.pdf`. Les cas métier critiques sont automatisés; les parcours visuels publiés, couleurs, DPI et critères de performance restent à confirmer sur poste interactif.
- Les scénarios de sauvegarde/restauration utilisent des bases temporaires isolées; ils contrôlent l'intégrité SQLite, les fichiers invalides/absents, la destination en lecture seule et la rétention des sept sauvegardes automatiques.
- Les tests utilisent des bases temporaires isolées; le smoke test WinForms STA ouvre notamment les formulaires d'audit et de sauvegarde/restauration.
- Les tests ouvrent des bases temporaires et contrôlent les transactions; le smoke test WinForms STA ouvre les formulaires de configuration, connexion, administration, vente, stocks, historique, rapports et tickets.
- Le binaire autonome a été lancé sans Visual Studio, a créé une base de test à schéma 1-3 et une sauvegarde vérifiée. L'installation, les raccourcis, la désinstallation et la réinstallation ont été exécutés; les empreintes de la base de test et de la base préexistante sont restées identiques après désinstallation/réinstallation.
- La fenêtre de démarrage a été observée, mais l'arbre UIA WinForms n'est pas resté accessible après lancement détaché. La navigation complète, la création UI de catégorie/produit, mouvements et vente restent à démontrer dans une session graphique interactive.

## Reste à réaliser

- Valider les parcours UI complets sur un poste interactif, aux DPI 100 %, 125 % et 150 %, ainsi que les objectifs de performance.
- Finaliser les paramètres magasin et créer une base de démonstration distincte conforme au cahier des charges.
- Les tests d'impression matérielle, d'arrêt forcé au milieu d'une restauration et de charge sur 5 000 produits / 50 000 ventes restent à réaliser.
- Une base préexistante `gestion-magasin.db` d'un schéma incompatible n'est pas importée; le fichier reste inchangé et l'application affiche un avertissement. Une procédure de migration est à définir si cette base doit être reprise.
