CREATE TABLE Utilisateur (
  UtilisateurId INTEGER PRIMARY KEY,
  Nom TEXT NOT NULL CHECK(length(trim(Nom)) BETWEEN 2 AND 120),
  Identifiant TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(Identifiant)) BETWEEN 3 AND 80),
  MotDePasseHash BLOB NOT NULL,
  Sel BLOB NOT NULL,
  ParametresHachage TEXT NOT NULL,
  Role TEXT NOT NULL CHECK(Role IN ('Administrateur','Vendeur')),
  Actif INTEGER NOT NULL DEFAULT 1 CHECK(Actif IN (0,1)),
  DateCreationUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE Categorie (
  CategorieId INTEGER PRIMARY KEY,
  Nom TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(Nom)) BETWEEN 1 AND 100),
  NomNormalise TEXT NOT NULL UNIQUE,
  Active INTEGER NOT NULL DEFAULT 1 CHECK(Active IN (0,1)),
  DateCreationUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE Produit (
  ProduitId INTEGER PRIMARY KEY,
  Reference TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK(length(trim(Reference)) BETWEEN 1 AND 60),
  Nom TEXT NOT NULL CHECK(length(trim(Nom)) BETWEEN 1 AND 160),
  CategorieId INTEGER NULL REFERENCES Categorie(CategorieId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  PrixVente INTEGER NOT NULL CHECK(PrixVente > 0),
  SeuilAlerte INTEGER NOT NULL DEFAULT 0 CHECK(SeuilAlerte >= 0),
  Actif INTEGER NOT NULL DEFAULT 1 CHECK(Actif IN (0,1)),
  DateCreationUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE Vente (
  VenteId INTEGER PRIMARY KEY,
  Numero TEXT NOT NULL UNIQUE,
  JetonValidation TEXT NOT NULL UNIQUE,
  DateHeureUtc TEXT NOT NULL,
  DateMetier TEXT NOT NULL,
  VendeurId INTEGER NOT NULL REFERENCES Utilisateur(UtilisateurId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  Total INTEGER NOT NULL CHECK(Total > 0),
  ModePaiement TEXT NOT NULL CHECK(ModePaiement IN ('Especes','MobileMoney','Carte')),
  MontantRecu INTEGER NOT NULL CHECK(MontantRecu > 0),
  MonnaieRendue INTEGER NOT NULL CHECK(MonnaieRendue >= 0),
  Statut TEXT NOT NULL CHECK(Statut IN ('Validee','Annulee'))
);

CREATE TABLE LigneVente (
  LigneVenteId INTEGER PRIMARY KEY,
  VenteId INTEGER NOT NULL REFERENCES Vente(VenteId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  ProduitId INTEGER NOT NULL REFERENCES Produit(ProduitId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  ReferenceSnapshot TEXT NOT NULL,
  NomSnapshot TEXT NOT NULL,
  Quantite INTEGER NOT NULL CHECK(Quantite > 0),
  PrixUnitaire INTEGER NOT NULL CHECK(PrixUnitaire > 0),
  SousTotal INTEGER NOT NULL CHECK(SousTotal > 0),
  UNIQUE(VenteId, ProduitId)
);

CREATE TABLE AnnulationVente (
  AnnulationVenteId INTEGER PRIMARY KEY,
  VenteId INTEGER NOT NULL UNIQUE REFERENCES Vente(VenteId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  AuteurId INTEGER NOT NULL REFERENCES Utilisateur(UtilisateurId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  DateHeureUtc TEXT NOT NULL,
  Motif TEXT NOT NULL CHECK(length(trim(Motif)) > 0)
);

CREATE TABLE MouvementStock (
  MouvementStockId INTEGER PRIMARY KEY,
  ProduitId INTEGER NOT NULL REFERENCES Produit(ProduitId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  AuteurId INTEGER NOT NULL REFERENCES Utilisateur(UtilisateurId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  DateHeureUtc TEXT NOT NULL,
  Type TEXT NOT NULL CHECK(Type IN ('Initial','Reapprovisionnement','Vente','AnnulationVente','CorrectionInventaire')),
  QuantiteSignee INTEGER NOT NULL CHECK(QuantiteSignee <> 0),
  Motif TEXT,
  LigneVenteId INTEGER NULL REFERENCES LigneVente(LigneVenteId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  AnnulationId INTEGER NULL REFERENCES AnnulationVente(AnnulationVenteId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  QuantiteAvant INTEGER NULL CHECK(QuantiteAvant IS NULL OR QuantiteAvant >= 0),
  QuantiteComptee INTEGER NULL CHECK(QuantiteComptee IS NULL OR QuantiteComptee >= 0),
  CHECK((Type='Initial' AND QuantiteSignee>0) OR (Type='Reapprovisionnement' AND QuantiteSignee>0) OR (Type='Vente' AND QuantiteSignee<0) OR (Type='AnnulationVente' AND QuantiteSignee>0) OR (Type='CorrectionInventaire' AND QuantiteSignee<>0))
);

CREATE TABLE JournalAudit (
  JournalAuditId INTEGER PRIMARY KEY,
  AuteurId INTEGER NULL REFERENCES Utilisateur(UtilisateurId) ON UPDATE RESTRICT ON DELETE RESTRICT,
  DateHeureUtc TEXT NOT NULL,
  Action TEXT NOT NULL,
  Entite TEXT NOT NULL,
  EntiteId TEXT,
  Details TEXT
);

CREATE TABLE Parametre (
  Cle TEXT PRIMARY KEY,
  Valeur TEXT NOT NULL,
  ModifieLeUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS VersionSchema (
  Numero INTEGER PRIMARY KEY,
  DateApplicationUtc TEXT NOT NULL,
  Description TEXT NOT NULL
);

CREATE UNIQUE INDEX UX_Mouvement_OneInitial ON MouvementStock(ProduitId) WHERE Type='Initial';
CREATE UNIQUE INDEX UX_Mouvement_OneSaleLine ON MouvementStock(LigneVenteId) WHERE Type='Vente';
CREATE UNIQUE INDEX UX_Mouvement_OneReversalPerLine ON MouvementStock(LigneVenteId) WHERE Type='AnnulationVente';
CREATE INDEX IX_Produit_Nom ON Produit(Nom COLLATE NOCASE);
CREATE INDEX IX_Produit_CategorieActif ON Produit(CategorieId, Actif);
CREATE INDEX IX_Vente_DateStatutVendeur ON Vente(DateMetier, Statut, VendeurId);
CREATE INDEX IX_LigneVente_Produit ON LigneVente(ProduitId);
CREATE INDEX IX_MouvementStock_ProduitDate ON MouvementStock(ProduitId, DateHeureUtc);
CREATE INDEX IX_JournalAudit_Date ON JournalAudit(DateHeureUtc);

CREATE VIEW VueStockCourant AS
SELECT p.ProduitId, p.Reference, p.Nom, p.SeuilAlerte,
       COALESCE(SUM(m.QuantiteSignee),0) AS QuantiteStock
FROM Produit p LEFT JOIN MouvementStock m ON m.ProduitId=p.ProduitId
GROUP BY p.ProduitId, p.Reference, p.Nom, p.SeuilAlerte;
