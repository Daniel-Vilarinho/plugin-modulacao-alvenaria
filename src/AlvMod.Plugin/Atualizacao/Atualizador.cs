using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AlvMod.Plugin.Atualizacao
{
    [DataContract]
    internal sealed class ReleaseGitHub
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "name")] public string Nome { get; set; }
        [DataMember(Name = "body")] public string Notas { get; set; }
        [DataMember(Name = "assets")] public AssetGitHub[] Arquivos { get; set; }

        public Version Versao => Atualizador.ParseVersao(Tag);

        public AssetGitHub Pacote =>
            Arquivos?.FirstOrDefault(a => a.Nome.StartsWith("AlvMod", StringComparison.OrdinalIgnoreCase)
                                          && a.Nome.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [DataContract]
    internal sealed class AssetGitHub
    {
        [DataMember(Name = "name")] public string Nome { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
    }

    /// <summary>
    /// Busca a última release no GitHub e troca os arquivos do bundle instalado.
    /// O Windows permite RENOMEAR uma DLL carregada: as atuais viram *.old e as novas entram
    /// no lugar. A versão nova vale na próxima vez que o AutoCAD abrir.
    /// </summary>
    internal static class Atualizador
    {
        private const string SufixoAntigo = ".old";

        public static string Repositorio =>
            typeof(Atualizador).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value;

        public static Version VersaoAtual =>
            ParseVersao(typeof(Atualizador).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        public static string VersaoAtualTexto =>
            typeof(Atualizador).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "?";

        /// <summary>Pasta "AlvMod.bundle" se o plugin foi instalado como bundle; senão null.</summary>
        public static string PastaBundle
        {
            get
            {
                var contents = Path.GetDirectoryName(typeof(Atualizador).Assembly.Location);
                var bundle = contents == null ? null : Directory.GetParent(contents)?.FullName;
                return bundle != null && bundle.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ? bundle : null;
            }
        }

        public static Version ParseVersao(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return new Version(0, 0, 0);
            texto = texto.Trim().TrimStart('v', 'V').Split('+', '-')[0];
            return Version.TryParse(texto, out var v) ? v : new Version(0, 0, 0);
        }

        /// <summary>Retorna a release mais nova que a atual, ou null se já está atualizado.</summary>
        public static ReleaseGitHub BuscarAtualizacao()
        {
            var release = BuscarUltimaRelease();
            return release != null && release.Pacote != null && release.Versao > VersaoAtual ? release : null;
        }

        public static ReleaseGitHub BuscarUltimaRelease()
        {
            var repo = Repositorio;
            if (string.IsNullOrEmpty(repo) || repo.StartsWith("OWNER/")) return null;

            using (var web = NovoWebClient())
            {
                web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                var json = web.DownloadData($"https://api.github.com/repos/{repo}/releases/latest");
                using (var ms = new MemoryStream(json))
                    return (ReleaseGitHub)new DataContractJsonSerializer(typeof(ReleaseGitHub)).ReadObject(ms);
            }
        }

        public static void Instalar(ReleaseGitHub release)
        {
            var bundle = PastaBundle ?? throw new InvalidOperationException(
                "O plugin não está instalado como bundle (foi carregado com NETLOAD). Use o instalar.ps1.");

            var temp = Path.Combine(Path.GetTempPath(), "AlvMod-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var zip = Path.Combine(temp, release.Pacote.Nome);
                using (var web = NovoWebClient())
                    web.DownloadFile(release.Pacote.Url, zip);

                var extraido = Path.Combine(temp, "x");
                ZipFile.ExtractToDirectory(zip, extraido);
                var novoBundle = Directory.GetDirectories(extraido, "*.bundle", SearchOption.AllDirectories).FirstOrDefault()
                                 ?? throw new InvalidDataException("O pacote baixado não contém uma pasta .bundle.");

                Substituir(novoBundle, bundle);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* temporário: ignora */ }
            }
        }

        /// <summary>Apaga os *.old deixados pela atualização anterior (já não estão carregados).</summary>
        public static void LimparAntigos()
        {
            var bundle = PastaBundle;
            if (bundle == null) return;
            foreach (var f in Directory.GetFiles(bundle, "*" + SufixoAntigo, SearchOption.AllDirectories))
                try { File.Delete(f); } catch { /* ainda em uso: fica para a próxima */ }
        }

        private static void Substituir(string origem, string destino)
        {
            var trocados = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var novo in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
                {
                    var alvo = Path.Combine(destino, novo.Substring(origem.Length).TrimStart('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo));

                    if (File.Exists(alvo))
                    {
                        var antigo = alvo + SufixoAntigo;
                        if (File.Exists(antigo)) File.Delete(antigo);
                        File.Move(alvo, antigo);
                        trocados.Add(alvo);
                    }
                    File.Copy(novo, alvo, true);
                }
            }
            catch
            {
                // Desfaz o que já foi trocado para não deixar o bundle pela metade.
                foreach (var alvo in trocados)
                    try { File.Copy(alvo + SufixoAntigo, alvo, true); } catch { }
                throw;
            }
        }

        private static WebClient NovoWebClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "AlvMod-Updater";
            if (web.Proxy != null) web.Proxy.Credentials = CredentialCache.DefaultCredentials;
            return web;
        }
    }
}
