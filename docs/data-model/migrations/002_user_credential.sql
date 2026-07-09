-- Migration 002 — User password credentials (ABIS-owned table)
-- ================================================================
-- A NEW ABIS-owned table backing the modern username/password login. The legacy
-- PowerBuilder ERP had NO application password column: it authenticated every user as a
-- real Oracle database account (login = a live Oracle CONNECT with the typed credentials;
-- see legacy/src/security/w_security_check.srw). There is therefore no legacy password
-- hash to migrate — the modern app keeps its own credential store here, one PBKDF2 hash
-- per security_user login.
--
-- The app also self-provisions this table at startup (AbisSchema.EnsureOwnedTablesAsync,
-- idempotent, ORA-00955-swallowing), so applying this file by hand is optional — it exists
-- for DBA visibility / a controlled rollout. No sequence needed (login_id is the key).
--
-- password_hash format (self-describing): pbkdf2-sha256$<iterations>$<b64 salt>$<b64 hash>.
-- must_change = 1 forces a change on first sign-in after an admin sets an initial password.
-- Only the derived hash is ever stored — never the plaintext.

CREATE TABLE abis_user_credential (
  login_id       VARCHAR2(64)   NOT NULL,
  password_hash  VARCHAR2(200)  NOT NULL,
  must_change    NUMBER(1)      DEFAULT 1 NOT NULL,
  updated_utc    DATE,
  updated_by     VARCHAR2(64),
  CONSTRAINT pk_abis_user_credential PRIMARY KEY (login_id)
);

-- One credential per login, case-insensitively (matches the app's LOWER(login_id) lookup).
CREATE UNIQUE INDEX ux_abis_user_cred_login ON abis_user_credential (UPPER(login_id));
