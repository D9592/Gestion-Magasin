CREATE UNIQUE INDEX IF NOT EXISTS UX_Audit_InitialStockOnce
  ON JournalAudit(EntiteId) WHERE Action='StockInitialise' AND Entite='Produit';
CREATE INDEX IF NOT EXISTS IX_Vente_NumeroStatut ON Vente(Numero,Statut);
CREATE INDEX IF NOT EXISTS IX_Annulation_Date ON AnnulationVente(DateHeureUtc);
CREATE INDEX IF NOT EXISTS IX_Mouvement_AuteurDate ON MouvementStock(AuteurId,DateHeureUtc);

CREATE TRIGGER IF NOT EXISTS TR_Mouvement_NoNegativeStock
BEFORE INSERT ON MouvementStock
WHEN COALESCE((SELECT SUM(QuantiteSignee) FROM MouvementStock WHERE ProduitId=NEW.ProduitId),0)+NEW.QuantiteSignee<0
BEGIN
  SELECT RAISE(ABORT,'STOCK_NEGATIF');
END;

CREATE TRIGGER IF NOT EXISTS TR_Mouvement_ValidateOrigin
BEFORE INSERT ON MouvementStock
WHEN (NEW.Type='Vente' AND (NEW.LigneVenteId IS NULL OR NEW.AnnulationId IS NOT NULL))
  OR (NEW.Type='AnnulationVente' AND (NEW.LigneVenteId IS NULL OR NEW.AnnulationId IS NULL))
  OR (NEW.Type IN ('Initial','Reapprovisionnement') AND (NEW.LigneVenteId IS NOT NULL OR NEW.AnnulationId IS NOT NULL))
  OR (NEW.Type='CorrectionInventaire' AND (NEW.LigneVenteId IS NOT NULL OR NEW.AnnulationId IS NOT NULL OR NEW.QuantiteAvant IS NULL OR NEW.QuantiteComptee IS NULL OR NEW.QuantiteSignee<>NEW.QuantiteComptee-NEW.QuantiteAvant))
BEGIN
  SELECT RAISE(ABORT,'MOUVEMENT_INCOHERENT');
END;

CREATE TRIGGER IF NOT EXISTS TR_Mouvement_ImmutableUpdate
BEFORE UPDATE ON MouvementStock
BEGIN
  SELECT RAISE(ABORT,'MOUVEMENT_IMMUABLE');
END;
CREATE TRIGGER IF NOT EXISTS TR_Mouvement_ImmutableDelete
BEFORE DELETE ON MouvementStock
BEGIN
  SELECT RAISE(ABORT,'MOUVEMENT_IMMUABLE');
END;

CREATE TRIGGER IF NOT EXISTS TR_LigneVente_ValidateTotal
BEFORE INSERT ON LigneVente
WHEN NEW.SousTotal<>NEW.Quantite*NEW.PrixUnitaire
BEGIN
  SELECT RAISE(ABORT,'TOTAL_LIGNE_INCOHERENT');
END;
CREATE TRIGGER IF NOT EXISTS TR_LigneVente_ImmutableUpdate
BEFORE UPDATE ON LigneVente
BEGIN
  SELECT RAISE(ABORT,'LIGNE_VENTE_IMMUABLE');
END;
CREATE TRIGGER IF NOT EXISTS TR_LigneVente_ImmutableDelete
BEFORE DELETE ON LigneVente
BEGIN
  SELECT RAISE(ABORT,'LIGNE_VENTE_IMMUABLE');
END;

CREATE TRIGGER IF NOT EXISTS TR_Vente_ValidatePayment
BEFORE INSERT ON Vente
WHEN (NEW.ModePaiement='Especes' AND (NEW.MontantRecu<NEW.Total OR NEW.MonnaieRendue<>NEW.MontantRecu-NEW.Total))
  OR (NEW.ModePaiement IN ('MobileMoney','Carte') AND (NEW.MontantRecu<>NEW.Total OR NEW.MonnaieRendue<>0))
BEGIN
  SELECT RAISE(ABORT,'PAIEMENT_INCOHERENT');
END;
CREATE TRIGGER IF NOT EXISTS TR_Vente_ProtectHistory
BEFORE UPDATE ON Vente
WHEN NOT (OLD.Statut='Validee' AND NEW.Statut='Annulee'
  AND NEW.Numero IS OLD.Numero AND NEW.JetonValidation IS OLD.JetonValidation
  AND NEW.DateHeureUtc IS OLD.DateHeureUtc AND NEW.DateMetier IS OLD.DateMetier
  AND NEW.VendeurId IS OLD.VendeurId AND NEW.Total IS OLD.Total
  AND NEW.ModePaiement IS OLD.ModePaiement AND NEW.MontantRecu IS OLD.MontantRecu
  AND NEW.MonnaieRendue IS OLD.MonnaieRendue)
BEGIN
  SELECT RAISE(ABORT,'VENTE_IMMUABLE');
END;
CREATE TRIGGER IF NOT EXISTS TR_Vente_RequireCancellation
BEFORE UPDATE OF Statut ON Vente
WHEN NEW.Statut='Annulee' AND NOT EXISTS(SELECT 1 FROM AnnulationVente WHERE VenteId=OLD.VenteId)
BEGIN
  SELECT RAISE(ABORT,'ANNULATION_ABSENTE');
END;
CREATE TRIGGER IF NOT EXISTS TR_Vente_ImmutableDelete
BEFORE DELETE ON Vente
BEGIN
  SELECT RAISE(ABORT,'VENTE_IMMUABLE');
END;

CREATE TRIGGER IF NOT EXISTS TR_Annulation_OnlyValidatedSale
BEFORE INSERT ON AnnulationVente
WHEN NOT EXISTS(SELECT 1 FROM Vente WHERE VenteId=NEW.VenteId AND Statut='Validee')
BEGIN
  SELECT RAISE(ABORT,'VENTE_NON_ANNULABLE');
END;
CREATE TRIGGER IF NOT EXISTS TR_Annulation_ImmutableUpdate
BEFORE UPDATE ON AnnulationVente
BEGIN
  SELECT RAISE(ABORT,'ANNULATION_IMMUABLE');
END;
CREATE TRIGGER IF NOT EXISTS TR_Annulation_ImmutableDelete
BEFORE DELETE ON AnnulationVente
BEGIN
  SELECT RAISE(ABORT,'ANNULATION_IMMUABLE');
END;
