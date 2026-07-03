namespace StorkDrop.Installer;

/// <summary>
/// A ready-to-send feed report persisted in the on-disk spool. The body is the fully encoded
/// CloudEvent (Base64) and the signature is precomputed, so delivery needs no config access.
/// </summary>
internal sealed record SpooledReport(string Url, string Signature, string ContentType, string Body);
