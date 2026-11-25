// Juicebar: bateria dos aparelhos sem fio, na barra de tarefas do Windows.
//
// Fontes (todas so de leitura: nao conecta, nao pareia, nao grava nada):
//  1. Windows: a bateria que o Windows ja recebe de qualquer aparelho Bluetooth conectado
//     (mouse, teclado, fone, controle...), agrupada por aparelho (ContainerId).
//  2. Bluetooth LE (GATT): aparelho LE conectado sem bateria no Windows -> le o servico
//     padrao de bateria (0x180F / 0x2A19).
//  3. Fast Pair do Google: fones que anunciam a bateria de cada lado (esquerdo, direito,
//     estojo). So vale para fone conectado a este PC (casado pelo nome).
//  4. USB e receptores 2.4 GHz: o padrao USB de bateria (HID) de qualquer marca e o protocolo
//     HID++ da Logitech (Lightspeed, Unifying, Bolt), Razer, Compx e Attack Shark X3. Receptor sem fio que ainda nao sabemos
//     ler aparece em "Sem leitura ainda" e no diagnostico.
//
// Atualizacoes: procura versao nova nos releases do GitHub; com versao nova, ponto amarelo na barra.
//
// Uso: juicebar.exe [--demo] [--testar arq] [--diagnostico arq] [--versao X]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.Win32.SafeHandles;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

[assembly: System.Reflection.AssemblyTitle("Juicebar")]
[assembly: System.Reflection.AssemblyProduct("Juicebar")]
[assembly: System.Reflection.AssemblyDescription("Bateria dos aparelhos sem fio na barra de tarefas")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 caiquebmaso - MIT")]
[assembly: System.Reflection.AssemblyVersion("1.2.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.2.1")]

// ================================================================ modelo
enum Tipo { Fone, Mouse, Teclado, Controle, Outro }

class Valor
{
    public string Rotulo;       // "", "E", "D", "Estojo"
    public int Nivel;           // 0-100, -1 = sem informacao
    public bool Carregando;
    public bool NaBarra = true; // estojo so na dica e nos detalhes
}

class Aparelho
{
    public string Id, Nome;
    public Tipo Tipo;
    public List<Valor> Valores = new List<Valor>();
    public string Fonte;
    public DateTime Visto;
    public bool PorFaixa;       // bateria em faixas que mudam com a carga (Logitech 0x1000): mostra a pior recente
    public int Menor { get { var v = Valores.Where(x => x.NaBarra && x.Nivel >= 0).Select(x => x.Nivel).ToList(); return v.Count == 0 ? -1 : v.Min(); } }
}

// ================================================================ fonte 1: Windows (SetupAPI)
static class Windows_
{
    static readonly Guid BAT = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");          // pid 2, byte
    static readonly Guid CONT = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");         // pid 2, guid
    static readonly Guid CONN = new Guid("83DA6326-97A6-4088-9453-A1923F573B29");         // pid 15, bool
    static readonly Guid NOME = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");         // 14 friendly, 2 desc

    static readonly Guid C_MOUSE = new Guid("4D36E96F-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_TECLADO = new Guid("4D36E96B-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_MEDIA = new Guid("4D36E96C-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_AUDIO = new Guid("C166523C-FE0C-4A94-A586-F1A80CFBBF3E");

    class Dev { public string Inst, Nome; public Guid Classe, Cont; public int Bat = -1; public bool? Conectado; }

    public class Grupo
    {
        public Guid Cont; public string Nome; public Tipo Tipo; public int Bat = -1; public bool Conectado;
        public ulong EnderecoLE;  // BTHLE\DEV_xxxx conectado
    }

    public static List<Grupo> Ler()
    {
        var devs = new List<Dev>();
        IntPtr h = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4);   // PRESENT | ALLCLASSES
        if (h == new IntPtr(-1)) return new List<Grupo>();
        try
        {
            var d = new SP_DEVINFO_DATA(); d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
            for (uint i = 0; SetupDiEnumDeviceInfo(h, i, ref d); i++)
            {
                var dev = new Dev { Classe = d.ClassGuid };
                var sb = new System.Text.StringBuilder(512); uint req;
                if (SetupDiGetDeviceInstanceIdW(h, ref d, sb, 512, out req)) dev.Inst = sb.ToString(); else continue;
                byte[] b;
                b = Prop(h, ref d, CONT, 2); if (b != null && b.Length == 16) dev.Cont = new Guid(b);
                b = Prop(h, ref d, BAT, 2); if (b != null && b.Length >= 1) dev.Bat = b[0];
                b = Prop(h, ref d, CONN, 15); if (b != null && b.Length >= 1) dev.Conectado = b[0] != 0;
                b = Prop(h, ref d, NOME, 14) ?? Prop(h, ref d, NOME, 2); if (b != null) dev.Nome = System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0');
                devs.Add(dev);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(h); }

        var saida = new List<Grupo>();
        foreach (var g in devs.Where(x => x.Cont != Guid.Empty).GroupBy(x => x.Cont))
        {
            var lista = g.ToList();
            var raiz = lista.FirstOrDefault(x => x.Inst.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) || x.Inst.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase));
            var comBat = lista.FirstOrDefault(x => x.Bat >= 0 && x.Bat <= 100);
            bool ehLE = raiz != null && raiz.Inst.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase);
            if (comBat == null && !ehLE) continue;
            if (comBat == null && raiz == null) continue;

            var gr = new Grupo { Cont = g.Key };
            gr.Nome = raiz != null && !string.IsNullOrEmpty(raiz.Nome) ? raiz.Nome : (comBat != null ? comBat.Nome : "Aparelho");
            gr.Bat = comBat != null ? comBat.Bat : -1;
            // vale o estado dos nos Bluetooth: a saida de audio (SWD\MMDEVAPI) fica "conectada" com o fone desligado
            var con = lista.Where(x => x.Conectado.HasValue && x.Inst.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)).ToList();
            if (con.Count == 0) con = lista.Where(x => x.Conectado.HasValue).ToList();
            gr.Conectado = con.Count == 0 || con.Any(x => x.Conectado.Value);
            if (lista.Any(x => x.Classe == C_MOUSE)) gr.Tipo = Tipo.Mouse;
            else if (lista.Any(x => x.Classe == C_TECLADO)) gr.Tipo = Tipo.Teclado;
            else if (lista.Any(x => x.Classe == C_MEDIA || x.Classe == C_AUDIO)) gr.Tipo = Tipo.Fone;
            else if (System.Text.RegularExpressions.Regex.IsMatch(gr.Nome ?? "", "controller|gamepad|controle|xbox|dualsense|dualshock", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) gr.Tipo = Tipo.Controle;
            else gr.Tipo = Tipo.Outro;
            if (ehLE && gr.Conectado)
            {
                var m = System.Text.RegularExpressions.Regex.Match(raiz.Inst, "DEV_([0-9A-F]{12})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) gr.EnderecoLE = Convert.ToUInt64(m.Groups[1].Value, 16);
            }
            if (comBat == null && gr.EnderecoLE == 0) continue;
            saida.Add(gr);
        }
        return saida;
    }

    static byte[] Prop(IntPtr h, ref SP_DEVINFO_DATA d, Guid fmt, uint pid)
    {
        var k = new DEVPROPKEY { fmtid = fmt, pid = pid };
        uint tipo, req;
        var buf = new byte[1024];
        if (!SetupDiGetDevicePropertyW(h, ref d, ref k, out tipo, buf, (uint)buf.Length, out req, 0)) return null;
        var r = new byte[req]; Array.Copy(buf, r, (int)req); return r;
    }

    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(IntPtr cls, string en, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr h, uint i, ref SP_DEVINFO_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr h, ref SP_DEVINFO_DATA d, System.Text.StringBuilder s, uint n, out uint req);
    [DllImport("setupapi.dll")] static extern bool SetupDiGetDevicePropertyW(IntPtr h, ref SP_DEVINFO_DATA d, ref DEVPROPKEY k, out uint tipo, byte[] buf, uint n, out uint req, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
}

// ================================================================ fonte 2: GATT (Bluetooth LE)
static class Gatt
{
    // espera uma operacao WinRT sem async/await (o csc do .NET Framework nao acha o GetAwaiter)
    static T Esperar<T>(Windows.Foundation.IAsyncOperation<T> op, int ms)
    {
        var fim = DateTime.Now.AddMilliseconds(ms);
        while (op.Status == Windows.Foundation.AsyncStatus.Started)
        {
            if (DateTime.Now > fim) { try { op.Cancel(); } catch { } return default(T); }
            Thread.Sleep(50);
        }
        return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
    }

    public static int Ler(ulong endereco)
    {
        try
        {
            using (var dev = Esperar(BluetoothLEDevice.FromBluetoothAddressAsync(endereco), 5000))
            {
                if (dev == null) return -1;
                var sv = Esperar(dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached), 5000);
                if (sv == null || sv.Status != GattCommunicationStatus.Success || sv.Services.Count == 0) return -1;
                var s = sv.Services[0];
                try
                {
                    var cs = Esperar(s.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached), 5000);
                    if (cs == null || cs.Status != GattCommunicationStatus.Success || cs.Characteristics.Count == 0) return -1;
                    var r = Esperar(cs.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached), 5000);
                    if (r == null || r.Status != GattCommunicationStatus.Success || r.Value == null || r.Value.Length < 1) return -1;
                    var b = new byte[1]; DataReader.FromBuffer(r.Value).ReadBytes(b);
                    return b[0] <= 100 ? (int)b[0] : -1;
                }
                finally { s.Dispose(); }
            }
        }
        catch { return -1; }
    }
}

// ================================================================ fonte 3: Fast Pair (anuncios LE)
class FastPair
{
    public class Leitura { public string Nome; public byte[] Bat; public DateTime Quando; }
    readonly BluetoothLEAdvertisementWatcher w = new BluetoothLEAdvertisementWatcher();
    readonly Dictionary<ulong, string> nomes = new Dictionary<ulong, string>();
    readonly Dictionary<ulong, byte[]> bats = new Dictionary<ulong, byte[]>();
    readonly Dictionary<string, Leitura> porNome = new Dictionary<string, Leitura>();
    public event Action Mudou;

    public FastPair() { w.ScanningMode = BluetoothLEScanningMode.Active; w.Received += Recebido; }
    public string Iniciar() { try { w.Start(); return null; } catch (Exception ex) { return ex.Message; } }
    public void Parar() { try { w.Stop(); } catch { } }

    public List<Leitura> Recentes()
    {
        lock (porNome) return porNome.Values.Where(x => (DateTime.Now - x.Quando).TotalMinutes < 10).ToList();
    }

    void Recebido(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        ulong end = e.BluetoothAddress;
        string nome = e.Advertisement.LocalName ?? "";
        byte[] bat = null;
        foreach (var d in e.Advertisement.DataSections)
        {
            if (d.DataType != 0x16 || d.Data.Length < 3) continue;
            var b = new byte[d.Data.Length];
            DataReader.FromBuffer(d.Data).ReadBytes(b);
            if (b[0] == 0x2C && b[1] == 0xFE) bat = Decodificar(b) ?? bat;
        }
        bool mudou = false;
        lock (porNome)
        {
            if (nome.Length > 0) nomes[end] = nome;
            if (bat != null) bats[end] = bat;
            string n; byte[] bb;
            if (nomes.TryGetValue(end, out n) && bats.TryGetValue(end, out bb))
            {
                Leitura l;
                mudou = !porNome.TryGetValue(n, out l) || !l.Bat.SequenceEqual(bb);
                porNome[n] = new Leitura { Nome = n, Bat = bb, Quando = DateTime.Now };
            }
        }
        if (mudou && Mudou != null) Mudou();
    }

    // 2C FE | flags | campos [cabecalho = tamanho<<4 | tipo]; tipo 3/4 = bateria E, D, estojo.
    // Cada byte: bit 7 = carregando; bits 0-6 = nivel (0x7F = desconhecido).
    public static byte[] Decodificar(byte[] b)
    {
        if (b.Length == 5) return null;
        int i = 3;
        while (i < b.Length)
        {
            int tam = b[i] >> 4, tipo = b[i] & 0x0F;
            i++;
            if (i + tam > b.Length) return null;
            if ((tipo == 3 || tipo == 4) && tam >= 1)
            {
                var r = new byte[] { 0x7F, 0x7F, 0x7F };
                for (int k = 0; k < Math.Min(tam, 3); k++) r[k] = b[i + k];
                return r;
            }
            i += tam;
        }
        return null;
    }

    public static string Normal(string s)
    {
        s = (s ?? "").ToUpperInvariant();
        foreach (var suf in new[] { "-LE", " LE", "(LE)" }) if (s.EndsWith(suf)) s = s.Substring(0, s.Length - suf.Length);
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }
}

// ================================================================ fonte 4: receptores USB / 2.4 GHz (HID)
// Cada colecao HID que o Windows mostra (um aparelho USB tem varias). Bluetooth fica de fora:
// esses ja vem pela fonte 1.
static class Hid
{
    public class Colecao
    {
        public string Caminho, Pai, Produto, Fabricante;
        public ushort Vid, Pid, UsagePage, Usage, InLen, OutLen, FeatLen;
        public bool Bluetooth;
    }

    public static List<Colecao> Listar()
    {
        var r = new List<Colecao>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr h = SetupDiGetClassDevsW(ref g, null, IntPtr.Zero, 0x12);    // PRESENT | DEVICEINTERFACE
        if (h == new IntPtr(-1)) return r;
        try
        {
            var ifd = new SP_DEVICE_INTERFACE_DATA(); ifd.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref g, i, ref ifd); i++)
            {
                var dd = new SP_DEVINFO_DATA(); dd.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                uint req;
                SetupDiGetDeviceInterfaceDetailW(h, ref ifd, IntPtr.Zero, 0, out req, ref dd);
                if (req == 0) continue;
                IntPtr buf = Marshal.AllocHGlobal((int)req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(h, ref ifd, buf, req, out req, ref dd)) continue;
                    var c = new Colecao { Caminho = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)) };
                    uint pai; var sb = new System.Text.StringBuilder(512);
                    if (CM_Get_Parent(out pai, dd.DevInst, 0) == 0 && CM_Get_Device_IDW(pai, sb, 512, 0) == 0) c.Pai = sb.ToString();
                    else c.Pai = c.Caminho;
                    string cam = c.Caminho.ToUpperInvariant(), p = c.Pai.ToUpperInvariant();
                    c.Bluetooth = cam.Contains("{00001124") || cam.Contains("{00001812") || p.StartsWith("BTH");
                    if (c.Bluetooth || Receptores.Travado(c.Caminho)) continue;
                    bool aberto = false;
                    var cc = c;
                    if (!Receptores.ComPrazo(() => { aberto = Descrever(cc); }, 2000)) { Receptores.MarcarTravado(c.Caminho); continue; }
                    if (aberto) r.Add(c);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(h); }
        return r;
    }

    // atributos, capacidades e textos de uma colecao (pode travar em aparelho que nao responde)
    static bool Descrever(Colecao c)
    {
                    using (var fh = Abrir(c.Caminho, false))
                    {
                        if (fh.IsInvalid) return false;
                        var at = new HIDD_ATTRIBUTES(); at.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                        if (HidD_GetAttributes(fh, ref at)) { c.Vid = at.VendorID; c.Pid = at.ProductID; }
                        IntPtr pp;
                        if (HidD_GetPreparsedData(fh, out pp))
                        {
                            try
                            {
                                HIDP_CAPS caps;
                                if (HidP_GetCaps(pp, out caps) == OK)
                                {
                                    c.UsagePage = caps.UsagePage; c.Usage = caps.Usage;
                                    c.InLen = caps.InputReportByteLength; c.OutLen = caps.OutputReportByteLength; c.FeatLen = caps.FeatureReportByteLength;
                                }
                            }
                            finally { HidD_FreePreparsedData(pp); }
                        }
                        c.Produto = Texto(fh, true); c.Fabricante = Texto(fh, false);
                    }
                    return true;
    }

    static string Texto(SafeFileHandle fh, bool produto)
    {
        var b = new byte[256];
        bool ok = produto ? HidD_GetProductString(fh, b, b.Length) : HidD_GetManufacturerString(fh, b, b.Length);
        if (!ok) return "";
        var s = System.Text.Encoding.Unicode.GetString(b);
        int z = s.IndexOf('\0'); return (z >= 0 ? s.Substring(0, z) : s).Trim();
    }

    // leitura e escrita (rw) ou so consulta (atributos, textos)
    public static SafeFileHandle Abrir(string caminho, bool rw, bool assincrono = false)
    {
        return CreateFileW(caminho, rw ? 0xC0000000u : 0u, 3, IntPtr.Zero, 3, assincrono ? 0x40000000u : 0u, IntPtr.Zero);
    }

    // so leitura, assincrono (para ler avisos que o aparelho manda sozinho)
    public static SafeFileHandle AbrirLeitura(string caminho)
    {
        return CreateFileW(caminho, 0x80000000u, 3, IntPtr.Zero, 3, 0x40000000u, IntPtr.Zero);
    }

    // ---- padrao USB de bateria: Battery Strength (0x06/0x20) ou AbsoluteStateOfCharge (0x85/0x65)
    public static int BateriaPadrao(Colecao c, out string como)
    {
        como = null;
        using (var fh = Abrir(c.Caminho, true))
        using (var f0 = fh.IsInvalid ? Abrir(c.Caminho, false) : null)
        {
            var uso = fh.IsInvalid ? f0 : fh;
            if (uso.IsInvalid) return -1;
            IntPtr pp;
            if (!HidD_GetPreparsedData(uso, out pp)) return -1;
            try
            {
                HIDP_CAPS caps;
                if (HidP_GetCaps(pp, out caps) != OK) return -1;
                foreach (int tipo in new[] { 2, 0 })            // 2 = feature, 0 = input
                {
                    ushort n = tipo == 2 ? caps.NumberFeatureValueCaps : caps.NumberInputValueCaps;
                    if (n == 0) continue;
                    var vc = new byte[72 * n];
                    if (HidP_GetValueCaps(tipo, vc, ref n, pp) != OK) continue;
                    for (int k = 0; k < n; k++)
                    {
                        int o = k * 72;
                        ushort up = BitConverter.ToUInt16(vc, o);
                        byte rid = vc[o + 2];
                        ushort link = BitConverter.ToUInt16(vc, o + 6);
                        bool faixa = vc[o + 16] != 0;
                        int lmin = BitConverter.ToInt32(vc, o + 40), lmax = BitConverter.ToInt32(vc, o + 44);
                        ushort u0 = BitConverter.ToUInt16(vc, o + 56), u1 = faixa ? BitConverter.ToUInt16(vc, o + 58) : u0;
                        ushort alvo = 0;
                        if (up == 0x06 && 0x20 >= u0 && 0x20 <= u1) alvo = 0x20;
                        else if (up == 0x85 && 0x65 >= u0 && 0x65 <= u1) alvo = 0x65;
                        if (alvo == 0) continue;
                        int len = tipo == 2 ? caps.FeatureReportByteLength : caps.InputReportByteLength;
                        if (len == 0) continue;
                        var rep = new byte[len]; rep[0] = rid;
                        bool lido = tipo == 2 ? HidD_GetFeature(uso, rep, len) : HidD_GetInputReport(uso, rep, len);
                        if (!lido) continue;
                        uint val;
                        if (HidP_GetUsageValue(tipo, up, link, alvo, out val, pp, rep, (uint)len) != OK) continue;
                        int pct = Escala((int)val, lmin, lmax, up == 0x85);
                        if (pct < 0) continue;
                        como = string.Format("{0} {1:X2}/{2:X2} relatório {3}", tipo == 2 ? "feature" : "input", up, alvo, rid);
                        return pct;
                    }
                }
            }
            finally { HidD_FreePreparsedData(pp); }
        }
        return -1;
    }

    // valor bruto -> 0-100 pela faixa logica do descritor (0x85/0x65 ja vem em %)
    public static int Escala(int val, int lmin, int lmax, bool jaEmPorcento)
    {
        if (jaEmPorcento || lmax <= lmin) return val >= 0 && val <= 100 ? val : -1;
        if (val < lmin || val > lmax) return -1;
        return (int)Math.Round((val - lmin) * 100.0 / (lmax - lmin));
    }

    public const int OK = 0x00110000;

    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS c);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int tipo, byte[] caps, ref ushort n, IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int tipo, ushort up, ushort link, ushort uso, out uint val, IntPtr pp, byte[] rep, uint len);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll")] static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll")] static extern bool HidD_GetManufacturerString(SafeFileHandle h, byte[] b, int n);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string p, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tpl);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevsW(ref Guid cls, string en, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr di, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, uint n, out uint req, ref SP_DEVINFO_DATA dd);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint pai, uint dev, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_IDW(uint dev, System.Text.StringBuilder s, int n, uint flags);
}

// ---- Logitech HID++ (receptores Lightspeed, Unifying e Bolt, e aparelhos Logitech por USB).
// Pergunta e resposta pelas colecoes de fabricante (0xFF00/0xFF43); so leitura.
class HidPP : IDisposable
{
    // "software id": um diferente a cada pergunta, para que a resposta atrasada de uma pergunta
    // (aparelho acordando) nao seja tomada pela da seguinte (ja deu 87% = 'W' do nome no lugar da bateria).
    // So ids livres: 07 OpenRGB, 0A LGSTrayEx, 0B Solaar, 0D G HUB, 0F firmware (lista do Solaar, base.py).
    static readonly byte[] SWS = { 0x08, 0x09, 0x0C, 0x0E };
    int swi;
    FileStream curto, longo;
    int outCurto, outLongo;
    readonly System.Collections.Concurrent.BlockingCollection<byte[]> fila = new System.Collections.Concurrent.BlockingCollection<byte[]>();
    volatile bool fechado;

    public static HidPP Abrir(IEnumerable<Hid.Colecao> grupo)
    {
        var x = new HidPP();
        foreach (var c in grupo.Where(c => c.UsagePage == 0xFF00 || c.UsagePage == 0xFF43))
        {
            if (c.OutLen == 7 && x.curto == null) { x.curto = x.Ligar(c, out x.outCurto); }
            else if (c.OutLen == 20 && x.longo == null) { x.longo = x.Ligar(c, out x.outLongo); }
        }
        if (x.curto == null && x.longo == null) { x.Dispose(); return null; }
        return x;
    }

    FileStream Ligar(Hid.Colecao c, out int outLen)
    {
        outLen = c.OutLen;
        var h = Hid.Abrir(c.Caminho, true, true);
        if (h.IsInvalid) return null;
        try
        {
            var fs = new FileStream(h, FileAccess.ReadWrite, 1, true);
            Ler(fs, c.InLen);
            return fs;
        }
        catch { h.Dispose(); return null; }
    }

    void Ler(FileStream fs, int len)
    {
        if (fechado || len <= 0) return;
        var b = new byte[len];
        try
        {
            fs.BeginRead(b, 0, len, ar =>
            {
                try { int n = fs.EndRead(ar); if (n > 0 && !fechado) { fila.Add(b); Ler(fs, len); } }
                catch { }
            }, null);
        }
        catch { }
    }

    // manda [id, aparelho, feature, funcao|sw, parametros] e espera a resposta certa
    public byte[] Pedir(byte idx, byte feat, int func, byte[] p, int ms)
    {
        byte[] lixo;
        while (fila.TryTake(out lixo)) { }
        bool usarLongo = longo != null && (curto == null || p.Length > 3);
        var fs = usarLongo ? longo : curto;
        if (fs == null) return null;
        var b = new byte[usarLongo ? outLongo : outCurto];
        byte sw = SWS[swi++ % SWS.Length];
        b[0] = (byte)(usarLongo ? 0x11 : 0x10); b[1] = idx; b[2] = feat; b[3] = (byte)((func << 4) | sw);
        Array.Copy(p, 0, b, 4, Math.Min(p.Length, b.Length - 4));
        try
        {
            var ar = fs.BeginWrite(b, 0, b.Length, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(1000)) return null;      // aparelho nao aceitou: desiste sem prender
            fs.EndWrite(ar);
        }
        catch { return null; }
        var fim = DateTime.Now.AddMilliseconds(ms);
        while (true)
        {
            int resta = (int)(fim - DateTime.Now).TotalMilliseconds;
            if (resta <= 0) return null;
            byte[] r;
            if (!fila.TryTake(out r, resta)) return null;
            if (r.Length < 5 || (r[0] != 0x10 && r[0] != 0x11) || r[1] != idx) continue;
            if ((r[2] == 0x8F || r[2] == 0xFF) && r[3] == feat && r[4] == b[3]) return null;   // erro (HID++ 1.0 / 2.0)
            if (r[2] == feat && r[3] == b[3]) { var s = new byte[r.Length - 4]; Array.Copy(r, 4, s, 0, s.Length); return s; }
        }
    }

    public void Dispose()
    {
        fechado = true;
        try { if (curto != null) curto.Dispose(); } catch { }
        try { if (longo != null) longo.Dispose(); } catch { }
    }

    // ---- consulta de um aparelho (indice 0xFF = o proprio aparelho USB; 1-6 = aparelhos no receptor)
    public class Info
    {
        public byte Indice; public int Versao; public string Nome; public int TipoLogi = -1;
        public int Nivel = -1; public bool Carregando; public int FeatureBateria;
    }

    public Info Consultar(byte idx)
    {
        var pg = Pedir(idx, 0x00, 1, new byte[] { 0, 0, 0x5A }, 400);   // ping: versao do protocolo
        if (pg == null || pg.Length < 3 || pg[2] != 0x5A || pg[0] < 2) return null;
        var i = new Info { Indice = idx, Versao = pg[0] * 10 + pg[1] };
        int fNome = Feature(idx, 0x0005);
        if (fNome > 0)
        {
            var q = Pedir(idx, (byte)fNome, 0, new byte[0], 400);
            int tam = q == null ? 0 : q[0];
            var sb = new System.Text.StringBuilder();
            while (sb.Length < tam && sb.Length < 64)
            {
                var t = Pedir(idx, (byte)fNome, 1, new byte[] { (byte)sb.Length }, 400);
                if (t == null) break;
                int antes = sb.Length;
                foreach (var ch in t) { if (ch == 0 || sb.Length >= tam) break; sb.Append((char)ch); }
                if (sb.Length == antes) break;
            }
            i.Nome = sb.Length >= tam ? sb.ToString().Trim() : null;     // nome cortado (pergunta sem resposta): melhor nenhum
            var ti = Pedir(idx, (byte)fNome, 2, new byte[0], 400);
            if (ti != null) i.TipoLogi = ti[0];
        }
        foreach (int f in new[] { 0x1004, 0x1000, 0x1001, 0x1F20 })
        {
            int fi = Feature(idx, f);
            if (fi <= 0) continue;
            var r = Pedir(idx, (byte)fi, f == 0x1004 ? 1 : 0, new byte[0], 500);
            int nivel; bool carr;
            if (r != null && Logi.Decodificar(f, r, out nivel, out carr)) { i.Nivel = nivel; i.Carregando = carr; i.FeatureBateria = f; break; }
        }
        return i;
    }

    int Feature(byte idx, int id)
    {
        var r = Pedir(idx, 0x00, 0, new byte[] { (byte)(id >> 8), (byte)id, 0 }, 400);
        return r == null ? -1 : r[0];
    }

    // ---- registro 0x2B5 do receptor Unifying (HID++ 1.0, leitura de registro longo):
    // sub 0x20+n-1 = pareamento (byte 7, 4 bits baixos = tipo), sub 0x40+n-1 = nome (byte 1 = tamanho).
    // Serve para aparelho que nao tem a feature 0x0005 (ex.: teclado K270). Fonte: Solaar (receiver.py).
    public byte[] Registro(byte sub, int ms)
    {
        if (curto == null) return null;
        byte[] lixo;
        while (fila.TryTake(out lixo)) { }
        var b = new byte[outCurto];
        b[0] = 0x10; b[1] = 0xFF; b[2] = 0x83; b[3] = 0xB5; b[4] = sub;
        try
        {
            var ar = curto.BeginWrite(b, 0, b.Length, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(1000)) return null;      // receptor nao aceitou: desiste sem prender
            curto.EndWrite(ar);
        }
        catch { return null; }
        var fim = DateTime.Now.AddMilliseconds(ms);
        while (true)
        {
            int resta = (int)(fim - DateTime.Now).TotalMilliseconds;
            if (resta <= 0) return null;
            byte[] r;
            if (!fila.TryTake(out r, resta)) return null;
            if (r.Length < 6 || r[1] != 0xFF) continue;
            if (r[0] == 0x10 && r[2] == 0x8F && r[3] == 0x83 && r[4] == 0xB5) return null;   // erro: registro/aparelho nao existe
            if (r[0] == 0x11 && r[2] == 0x83 && r[3] == 0xB5 && r[4] == sub) { var s = new byte[r.Length - 4]; Array.Copy(r, 4, s, 0, s.Length); return s; }
        }
    }

    // nome e tipo que o receptor guardou no pareamento (null/-1 se nao for Unifying)
    public void Pareamento(byte idx, out string nome, out int tipo10)
    {
        nome = null; tipo10 = -1;
        if (idx < 1 || idx > 6) return;
        nome = Logi.NomeDoRegistro(Registro((byte)(0x40 + idx - 1), 400));
        var p = Registro((byte)(0x20 + idx - 1), 400);
        if (p != null && p.Length > 7) tipo10 = p[7] & 0x0F;
    }
}

// ---- Razer: relatorio de 90 bytes (feature, id 0) na colecao principal; o mesmo em todos os
// aparelhos sem fio da marca. Comando 0x07/0x80 = bateria (0-255), 0x07/0x84 = carregando.
static class Razer
{
    static readonly byte[] TRANSACOES = { 0x1F, 0x3F, 0xFF, 0x9F };

    public static byte[] Montar(byte transacao, byte classe, byte comando, byte tamanho)
    {
        var b = new byte[91];                       // [0] = id do relatorio
        b[2] = transacao; b[6] = tamanho; b[7] = classe; b[8] = comando;
        b[89] = Crc(b);
        return b;
    }

    // XOR dos bytes 2..87 do relatorio (3..88 no buffer com o id na frente)
    public static byte Crc(byte[] b) { byte c = 0; for (int i = 3; i < 89; i++) c ^= b[i]; return c; }

    // resposta: [1] estado (2 = ok), [7]/[8] classe/comando, [10] valor
    public static int Valor(byte[] r, byte classe, byte comando)
    {
        if (r == null || r.Length < 91 || r[1] != 0x02 || r[7] != classe || r[8] != comando) return -1;
        return r[10];
    }

    public static int PorcentoDe(int bruto) { return bruto < 0 ? -1 : (int)Math.Round(bruto * 100.0 / 255); }

    public static bool Ler(Hid.Colecao c, out int nivel, out bool carregando)
    {
        nivel = -1; carregando = false;
        using (var rw = Hid.Abrir(c.Caminho, true))
        using (var so = rw.IsInvalid ? Hid.Abrir(c.Caminho, false) : null)
        {
            var h = rw.IsInvalid ? so : rw;
            if (h.IsInvalid) return false;
            foreach (var t in TRANSACOES)
            {
                int bruto = Perguntar(h, t, 0x07, 0x80);
                if (bruto < 0) continue;
                if (bruto == 0) return false;               // com fio / sem bateria
                nivel = PorcentoDe(bruto);
                carregando = Perguntar(h, t, 0x07, 0x84) == 1;
                return true;
            }
        }
        return false;
    }

    static int Perguntar(SafeFileHandle h, byte transacao, byte classe, byte comando)
    {
        var p = Montar(transacao, classe, comando, 0x02);
        if (!HidD_SetFeature(h, p, p.Length)) return -1;
        for (int tentativa = 0; tentativa < 4; tentativa++)
        {
            Thread.Sleep(60);
            var r = new byte[91];
            if (!HidD_GetFeature(h, r, r.Length)) return -1;
            if (r[1] == 0x01) continue;                     // ocupado: espera mais um pouco
            return Valor(r, classe, comando);
        }
        return -1;
    }

    [DllImport("hid.dll")] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
}

// ---- Compx (VID 3554): receptores 2.4 GHz de mouses Redragon, Kysona, Pulsar, ATK/VXE.
// Relatorio 0x08 de 17 bytes na colecao de fabricante (interface 1): pedido por SET_REPORT
// (HidD_SetOutputReport, o receptor so escuta transferencia de controle), resposta como input.
// [0]=0x08 [1]=comando ... [16]=0x55 - soma(0..15). 0x03 status: [6]=1 mouse acordado.
// 0x04 energia: [6]=%, [7]=1 cabo ligado, [8..9]=mV (big-endian). Fonte: Linux drivers/hid/hid-pulsar.c.
static class Compx
{
    public const byte STATUS = 0x03, ENERGIA = 0x04;

    public static byte[] Montar(byte cmd)
    {
        var p = new byte[17]; p[0] = 0x08; p[1] = cmd;
        p[16] = Soma(p);
        return p;
    }

    public static byte Soma(byte[] b) { int s = 0; for (int i = 0; i < 16; i++) s += b[i]; return (byte)(0x55 - s); }

    public static bool Valida(byte[] r, byte cmd) { return r != null && r.Length >= 17 && r[0] == 0x08 && r[1] == cmd && r[16] == Soma(r); }

    // resposta do 0x04 -> %, carregando, mV (cabo ligado e < 100% = carregando, como no Linux)
    public static bool Decodificar(byte[] r, out int nivel, out bool carregando, out int mv)
    {
        nivel = -1; carregando = false; mv = 0;
        if (!Valida(r, ENERGIA) || r[6] > 100 || r[7] > 1) return false;
        nivel = r[6]; carregando = r[7] == 1 && r[6] < 100; mv = (r[8] << 8) | r[9];
        return true;
    }

    // estado: "ok", "dormindo" (receptor respondeu que o mouse nao esta disponivel) ou "sem resposta"
    public static bool Ler(Hid.Colecao c, out int nivel, out bool carregando, out int mv, out string estado)
    {
        nivel = -1; carregando = false; mv = 0; estado = "sem resposta";
        var h = Hid.Abrir(c.Caminho, true, true);
        if (h.IsInvalid) { h.Dispose(); estado = "não abriu"; return false; }
        var fila = new System.Collections.Concurrent.BlockingCollection<byte[]>();
        bool fechado = false;
        using (var fs = new FileStream(h, FileAccess.ReadWrite, 1, true))
        {
            Action ler = null;
            ler = () =>
            {
                var b = new byte[c.InLen];
                try { fs.BeginRead(b, 0, b.Length, ar => { try { if (fs.EndRead(ar) > 0 && !fechado) { fila.Add(b); ler(); } } catch { } }, null); } catch { }
            };
            ler();
            try
            {
                var st = Perguntar(h, fila, STATUS);
                if (st == null) return false;
                if (st[6] != 1) { estado = "dormindo"; return false; }
                var r = Perguntar(h, fila, ENERGIA);
                if (!Decodificar(r, out nivel, out carregando, out mv)) return false;
                estado = "ok";
                return true;
            }
            finally { fechado = true; }
        }
    }

    static byte[] Perguntar(SafeFileHandle h, System.Collections.Concurrent.BlockingCollection<byte[]> fila, byte cmd)
    {
        byte[] lixo;
        while (fila.TryTake(out lixo)) { }
        var p = Montar(cmd);
        bool enviado = false;                                  // SET_REPORT pode esperar o receptor para sempre: 1 s
        if (!Receptores.ComPrazo(() => { enviado = HidD_SetOutputReport(h, p, p.Length); }, 1000) || !enviado) return null;
        var fim = DateTime.Now.AddMilliseconds(300);
        while (true)
        {
            int resta = (int)(fim - DateTime.Now).TotalMilliseconds;
            byte[] r;
            if (resta <= 0 || !fila.TryTake(out r, resta)) return null;
            if (Valida(r, cmd)) return r;
        }
    }

    [DllImport("hid.dll")] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int n);
}

// ---- Attack Shark X3 (VID 1D57, PID FA60 receptor 2.4 GHz / FA61 cabo, chip Beken).
// So leitura: a colecao de status (pagina 0x000A, input de 5 bytes) manda sozinha, a cada ~2 s,
// 03 10 VV FF xx: VV = tensao em 1/16 V, FF bit 0 = na bateria (0 = cabo ligado). 03 10 03 00 FF
// e um aviso vazio (descartado). Nada chega com o mouse dormindo. Nunca escrever na colecao de
// configuracao (0x000B): ela grava na memoria do mouse. Fonte: github.com/prodmutant/Attack-Shark-X3 (MIT).
static class AttackShark
{
    public static bool Eh(ushort vid, ushort pid) { return vid == 0x1D57 && (pid == 0xFA60 || pid == 0xFA61); }

    // curva do projeto acima (V, %), ancorada em 4,00 V = 70% lido no programa da marca
    static readonly double[,] CURVA = { { 3.27, 0 }, { 3.50, 3 }, { 3.60, 7 }, { 3.68, 12 }, { 3.72, 18 }, { 3.76, 26 }, { 3.80, 35 }, { 3.84, 45 },
        { 3.88, 54 }, { 3.92, 61 }, { 3.96, 66 }, { 4.00, 70 }, { 4.05, 78 }, { 4.10, 85 }, { 4.15, 93 }, { 4.20, 100 } };

    public static int PorTensao(double v)
    {
        if (v <= CURVA[0, 0]) return 0;
        int n = CURVA.GetLength(0);
        if (v >= CURVA[n - 1, 0]) return 100;
        for (int k = 1; k < n; k++)
            if (v <= CURVA[k, 0])
            {
                double t = (v - CURVA[k - 1, 0]) / (CURVA[k, 0] - CURVA[k - 1, 0]);
                return (int)Math.Round(CURVA[k - 1, 1] + t * (CURVA[k, 1] - CURVA[k - 1, 1]));
            }
        return 100;
    }

    // um aviso -> nivel (-1 se a tensao for a do cabo), carregando, mV; false se nao for aviso de bateria valido
    public static bool Decodificar(byte[] r, out int nivel, out bool carregando, out int mv)
    {
        nivel = -1; carregando = false; mv = 0;
        if (r == null || r.Length < 4 || r[0] != 0x03 || r[1] != 0x10) return false;
        double v = r[2] / 16.0;
        if (v < 3.0 || v > 5.5) return false;                  // aviso vazio (03 10 03 00 FF) ou lixo
        mv = (int)Math.Round(v * 1000);
        bool naBateria = (r[3] & 0x01) != 0;
        carregando = v > 4.35 || !naBateria;
        nivel = v > 4.35 ? -1 : PorTensao(v);                  // acima de 4,35 V e a tensao do USB, nao da pilha
        return true;
    }

    // le ate 5 avisos em ate 5 s e fica com a tensao que mais apareceu
    public static bool Ler(Hid.Colecao c, out int nivel, out bool carregando, out int mv, out string estado)
    {
        nivel = -1; carregando = false; mv = 0; estado = "dormindo";
        var h = Hid.AbrirLeitura(c.Caminho);
        if (h.IsInvalid) { h.Dispose(); estado = "não abriu"; return false; }
        var lidos = new List<byte[]>();
        using (var fs = new FileStream(h, FileAccess.Read, 1, true))
        {
            var fim = DateTime.Now.AddMilliseconds(5000);
            while (lidos.Count < 5)
            {
                int resta = (int)(fim - DateTime.Now).TotalMilliseconds;
                if (resta <= 0) break;
                var b = new byte[Math.Max((int)c.InLen, 5)];
                IAsyncResult ar;
                try { ar = fs.BeginRead(b, 0, b.Length, null, null); } catch { break; }
                if (!ar.AsyncWaitHandle.WaitOne(resta)) break;      // fechar o arquivo cancela a leitura pendente
                int n;
                try { n = fs.EndRead(ar); } catch { break; }
                int nv, m; bool cr;
                if (n >= 4 && Decodificar(b, out nv, out cr, out m)) lidos.Add(b);
            }
        }
        if (lidos.Count == 0) return false;
        var moda = lidos.GroupBy(b => b[2] << 8 | (b[3] & 1)).OrderByDescending(gr => gr.Count()).ThenByDescending(gr => lidos.LastIndexOf(gr.Last())).First().Last();
        Decodificar(moda, out nivel, out carregando, out mv);
        estado = "ok";
        return true;
    }
}

// decodificacao das respostas de bateria (separada para poder testar com respostas gravadas)
static class Logi
{
    public static bool Decodificar(int feature, byte[] p, out int nivel, out bool carregando)
    {
        nivel = -1; carregando = false;
        if (p == null || p.Length < 3) return false;
        switch (feature)
        {
            case 0x1004:                                   // Unified Battery: %, nivel, estado, energia externa
            {
                int soc = p[0], faixa = p[1], st = p[2];
                if (soc > 0 && soc <= 100) nivel = soc;
                else if ((faixa & 8) != 0) nivel = 100;
                else if ((faixa & 4) != 0) nivel = 60;
                else if ((faixa & 2) != 0) nivel = 20;
                else if ((faixa & 1) != 0) nivel = 5;
                carregando = st == 1 || st == 2;
                if (st == 3 && nivel < 0) nivel = 100;
                return nivel >= 0;
            }
            case 0x1000:                                   // Battery Status: %, proximo nivel, estado
            {
                int st = p[2];
                nivel = p[0] > 0 && p[0] <= 100 ? p[0] : (st == 3 ? 100 : -1);
                carregando = st == 1 || st == 2 || st == 4;
                return nivel >= 0;
            }
            case 0x1001:                                   // Battery Voltage: mV (2 bytes), flags
            case 0x1F20:                                   // ADC Measurement (headsets): mV (2 bytes), flags
            {
                int mv = (p[0] << 8) | p[1];
                if (mv < 2500 || mv > 5000) return false;
                nivel = PorTensao(mv);
                carregando = feature == 0x1001 && (p[2] & 0x80) != 0;
                return true;
            }
        }
        return false;
    }

    // curva tipica de pilha de litio (mV -> %), com interpolacao entre os pontos
    static readonly int[,] CURVA = { { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 }, { 3778, 40 }, { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 } };

    public static int PorTensao(int mv)
    {
        if (mv >= CURVA[0, 0]) return 100;
        for (int k = 1; k < CURVA.GetLength(0); k++)
        {
            if (mv >= CURVA[k, 0])
            {
                double t = (mv - CURVA[k, 0]) / (double)(CURVA[k - 1, 0] - CURVA[k, 0]);
                return (int)Math.Round(CURVA[k, 1] + t * (CURVA[k - 1, 1] - CURVA[k, 1]));
            }
        }
        return 0;
    }

    public static Tipo TipoDe(int t)
    {
        switch (t)
        {
            case 0: case 2: return Tipo.Teclado;
            case 3: case 4: case 5: return Tipo.Mouse;
            case 8: return Tipo.Fone;
            case 10: case 11: case 12: return Tipo.Controle;
            default: return Tipo.Outro;
        }
    }

    // resposta do registro 0x2B5 sub 0x4N (sem o cabecalho): [sub, tamanho, nome...]
    public static string NomeDoRegistro(byte[] r)
    {
        if (r == null || r.Length < 3 || r[1] == 0 || r[1] > r.Length - 2) return null;
        var s = System.Text.Encoding.ASCII.GetString(r, 2, r[1]).Trim('\0', ' ');
        return s.Length > 0 ? s : null;
    }

    // tipo do pareamento HID++ 1.0 (registro 0x2B5 sub 0x2N, byte 7 & 0x0F), tabela do Solaar
    public static Tipo TipoDe10(int t)
    {
        switch (t)
        {
            case 0x01: case 0x03: return Tipo.Teclado;          // teclado, teclado numerico
            case 0x02: case 0x08: case 0x09: return Tipo.Mouse;  // mouse, trackball, touchpad
            case 0x0B: case 0x0C: return Tipo.Controle;          // gamepad, joystick
            case 0x0D: return Tipo.Fone;
            default: return Tipo.Outro;
        }
    }

    public static string NomeDoTipo10(int t)
    {
        switch (t)
        {
            case 0x01: return "teclado"; case 0x02: return "mouse"; case 0x03: return "teclado numérico"; case 0x04: return "apresentador";
            case 0x07: return "controle remoto"; case 0x08: return "trackball"; case 0x09: return "touchpad"; case 0x0A: return "mesa digitalizadora";
            case 0x0B: return "gamepad"; case 0x0C: return "joystick"; case 0x0D: return "headset";
            default: return t < 0 ? "?" : "tipo " + t;
        }
    }

    public static string NomeDoTipo(int t)
    {
        string[] n = { "teclado", "controle remoto", "teclado numérico", "mouse", "touchpad", "trackball", "apresentador", "receptor", "headset", "webcam", "volante", "joystick", "gamepad", "dock", "caixa de som", "microfone" };
        return t >= 0 && t < n.Length ? n[t] : (t < 0 ? "?" : "tipo " + t);
    }
}

// ---- leitura de todos os aparelhos USB/2.4 GHz e o texto do diagnostico
static class Receptores
{
    public class Resultado
    {
        public List<Aparelho> Aparelhos = new List<Aparelho>();
        public List<string> SemLeitura = new List<string>();   // receptor reconhecido que ainda nao sabemos ler
        public string Diagnostico = "";
    }

    static readonly System.Text.RegularExpressions.Regex SEM_FIO = new System.Text.RegularExpressions.Regex(
        "wireless|receiver|dongle|receptor|2\\.4|lightspeed|hyperspeed|slipstream|nano|unifying|bolt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static readonly object trava = new object();

    public static Resultado Ler()
    {
        if (!Monitor.TryEnter(trava, 20000)) return new Resultado { Diagnostico = "outra leitura USB ainda está rodando\r\n" };
        try { return LerSozinho(); }
        finally { Monitor.Exit(trava); }
    }

    // Aparelho que nao responde pode prender uma chamada do Windows para sempre. Cada aparelho
    // roda numa linha propria com prazo; se estourar, fica 10 min sem ser perguntado.
    public static readonly Dictionary<string, DateTime> Travados = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    public static bool ComPrazo(Action a, int ms)
    {
        var t = new Thread(() => { try { a(); } catch { } }) { IsBackground = true, Name = "hid" };
        t.Start();
        return t.Join(ms);
    }

    public static bool Travado(string chave)
    {
        lock (Travados)
        {
            DateTime q;
            if (!Travados.TryGetValue(chave, out q)) return false;
            if ((DateTime.Now - q).TotalMinutes < 10) return true;
            Travados.Remove(chave); return false;
        }
    }

    public static void MarcarTravado(string chave) { lock (Travados) Travados[chave] = DateTime.Now; }

    static Resultado LerSozinho()
    {
        var res = new Resultado();
        var d = new System.Text.StringBuilder();
        List<Hid.Colecao> todas;
        try { todas = Hid.Listar(); } catch (Exception ex) { res.Diagnostico = "HID: erro ao listar: " + ex.Message; return res; }
        var lidos = new HashSet<string>();
        var semLeitura = new List<Tuple<string, string>>();
        lock (Travados) foreach (var k in Travados.Keys) d.AppendLine("travado (pulado por 10 min): " + k);
        foreach (var g in todas.Where(c => !c.Bluetooth).GroupBy(c => c.Pai, StringComparer.OrdinalIgnoreCase))
        {
            if (Travado(g.Key)) continue;
            var parcial = new Grupo();
            var dg = new System.Text.StringBuilder();
            var grupo = g;
            // 20 s: o receptor Unifying pergunta por ate 6 aparelhos, e aparelho acordando demora
            bool acabou = ComPrazo(() => LerGrupo(grupo, parcial, dg), PRAZO_GRUPO_MS);
            lock (dg) d.Append(dg.ToString());
            if (!acabou)
            {
                MarcarTravado(g.Key);
                d.AppendLine("   TRAVOU: não respondeu em " + PRAZO_GRUPO_MS / 1000 + " s (" + g.Key + ")");
                continue;
            }
            res.Aparelhos.AddRange(parcial.Aparelhos);
            if (parcial.Lido) lidos.Add(parcial.Vp);
            if (parcial.SemLeitura != null) semLeitura.Add(Tuple.Create(parcial.Vp, parcial.SemLeitura));
        }
        // o receptor aparece como varias interfaces USB; se uma foi lida, as outras nao contam como "sem leitura"
        res.SemLeitura.AddRange(semLeitura.Where(t => !lidos.Contains(t.Item1)).Select(t => t.Item2).Distinct());
        res.Diagnostico = d.ToString();
        return res;
    }

    public const int PRAZO_GRUPO_MS = 20000;

    // resultado de um aparelho USB (as interfaces de um mesmo pai)
    class Grupo
    {
        public List<Aparelho> Aparelhos = new List<Aparelho>();
        public string Vp, SemLeitura;
        public bool Lido;
    }

    static void LerGrupo(IGrouping<string, Hid.Colecao> g, Grupo res, System.Text.StringBuilder dd)
    {
        var d = new Linhas(dd);
        {
            var cols = g.ToList();
            var c0 = cols[0];
            string produto = cols.Select(c => c.Produto).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";
            string fab = cols.Select(c => c.Fabricante).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";
            d.AppendLine(string.Format("{0:X4}:{1:X4}  {2} {3}", c0.Vid, c0.Pid, fab, produto).TrimEnd());
            d.AppendLine("   coleções: " + string.Join(", ", cols.Select(c => string.Format("{0:X4}/{1:X4} (in {2}, out {3}, feat {4})", c.UsagePage, c.Usage, c.InLen, c.OutLen, c.FeatLen))));
            bool lido = false;

            // 1. padrao USB de bateria
            foreach (var c in cols)
            {
                string como; int n = -1;
                try { n = Hid.BateriaPadrao(c, out como); } catch { como = null; }
                if (n < 0) continue;
                d.AppendLine("   padrão USB: " + n + "% (" + como + ")");
                var a = new Aparelho { Id = "hid:" + g.Key, Nome = string.IsNullOrEmpty(produto) ? "Aparelho USB" : produto, Tipo = TipoPelaColecao(cols), Fonte = "USB" };
                a.Valores.Add(new Valor { Rotulo = "", Nivel = n });
                res.Aparelhos.Add(a); lido = true; break;
            }

            // 2. Logitech HID++
            if (!lido && c0.Vid == 0x046D && cols.Any(c => c.UsagePage == 0xFF00 || c.UsagePage == 0xFF43))
            {
                HidPP pp = null;
                try { pp = HidPP.Abrir(cols); } catch { }
                if (pp == null) d.AppendLine("   HID++: não consegui abrir as coleções do fabricante");
                else using (pp)
                {
                    foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                    {
                        HidPP.Info i = null;
                        try { i = pp.Consultar(idx); } catch { }
                        if (i == null) continue;
                        // sem nome/tipo pela feature 0x0005: pergunta ao receptor (pareamento Unifying)
                        string nomeR = null; int tipoR = -1;
                        if (idx != 0xFF && (string.IsNullOrEmpty(i.Nome) || i.TipoLogi < 0))
                        {
                            try { pp.Pareamento(idx, out nomeR, out tipoR); } catch { }
                            if (string.IsNullOrEmpty(i.Nome) && nomeR != null) i.Nome = nomeR;
                        }
                        string tipoTxt = i.TipoLogi >= 0 ? Logi.NomeDoTipo(i.TipoLogi) : Logi.NomeDoTipo10(tipoR) + (tipoR >= 0 ? " (receptor)" : "");
                        d.AppendLine(string.Format("   HID++ {0}: protocolo {1}.{2}, \"{3}\", {4}, bateria: {5}", idx == 0xFF ? "direto" : "aparelho " + idx, i.Versao / 10, i.Versao % 10, i.Nome, tipoTxt,
                            i.Nivel < 0 ? "não tem" : string.Format("{0}%{1} (feature {2:X4})", i.Nivel, i.Carregando ? " carregando" : "", i.FeatureBateria)));
                        if (i.Nivel < 0 || i.TipoLogi == 7) continue;
                        var tipo = i.TipoLogi >= 0 ? Logi.TipoDe(i.TipoLogi) : Logi.TipoDe10(tipoR);
                        var a = new Aparelho { Id = string.Format("logi:{0}:{1}", g.Key, idx), Nome = string.IsNullOrEmpty(i.Nome) ? "Aparelho Logitech" : "Logitech " + i.Nome, Tipo = tipo, Fonte = "Logitech" };
                        if (a.Nome.StartsWith("Logitech Logitech")) a.Nome = a.Nome.Substring(9);
                        a.PorFaixa = i.FeatureBateria == 0x1000;
                        a.Valores.Add(new Valor { Rotulo = "", Nivel = i.Nivel, Carregando = i.Carregando });
                        res.Aparelhos.Add(a); lido = true;
                    }
                }
            }

            // 3. Razer
            if (!lido && c0.Vid == 0x1532)
            {
                foreach (var c in cols.Where(x => x.FeatLen == 91))
                {
                    int n; bool carr = false; bool ok = false;
                    try { ok = Razer.Ler(c, out n, out carr); } catch { n = -1; }
                    d.AppendLine("   Razer " + string.Format("{0:X4}/{1:X4}", c.UsagePage, c.Usage) + ": " + (ok ? n + "%" + (carr ? " carregando" : "") : "sem resposta de bateria"));
                    if (!ok) continue;
                    var a = new Aparelho { Id = "razer:" + g.Key, Nome = string.IsNullOrEmpty(produto) ? "Aparelho Razer" : produto, Tipo = TipoPelaColecao(cols), Fonte = "Razer" };
                    if (!a.Nome.StartsWith("Razer", StringComparison.OrdinalIgnoreCase)) a.Nome = "Razer " + a.Nome;
                    a.Valores.Add(new Valor { Rotulo = "", Nivel = n, Carregando = carr });
                    res.Aparelhos.Add(a); lido = true; break;
                }
            }

            // 4. Compx (Redragon, Kysona, Pulsar...)
            if (!lido && c0.Vid == 0x3554)
            {
                foreach (var c in cols.Where(x => x.InLen == 17 && x.OutLen == 17 && x.UsagePage >= 0xFF00))
                {
                    int n, mv; bool carr; string estado; bool ok = false;
                    try { ok = Compx.Ler(c, out n, out carr, out mv, out estado); } catch (Exception ex) { n = -1; carr = false; mv = 0; estado = "erro: " + ex.Message; }
                    d.AppendLine(string.Format("   Compx {0:X4}/{1:X4}: {2}", c.UsagePage, c.Usage,
                        ok ? string.Format("{0}%{1} ({2} mV)", n, carr ? " carregando" : "", mv) : estado == "dormindo" ? "mouse dormindo ou desligado" : estado));
                    if (!ok) { if (estado == "dormindo") lido = true; continue; }   // dormindo: nao vai para "sem leitura"
                    var a = new Aparelho { Id = "compx:" + g.Key, Nome = string.IsNullOrEmpty(produto) || SEM_FIO.IsMatch(produto) ? "Mouse 2.4 GHz" : produto, Tipo = TipoPelaColecao(cols), Fonte = "Compx" };
                    if (a.Tipo == Tipo.Outro) a.Tipo = Tipo.Mouse;
                    a.Valores.Add(new Valor { Rotulo = "", Nivel = n, Carregando = carr });
                    res.Aparelhos.Add(a); lido = true; break;
                }
            }

            // 5. Attack Shark X3 (so leitura da colecao de status)
            if (!lido && AttackShark.Eh(c0.Vid, c0.Pid))
            {
                foreach (var c in cols.Where(x => x.UsagePage == 0x000A && x.InLen >= 5))
                {
                    int n, mv; bool carr; string estado; bool ok = false;
                    try { ok = AttackShark.Ler(c, out n, out carr, out mv, out estado); } catch (Exception ex) { n = -1; carr = false; mv = 0; estado = "erro: " + ex.Message; }
                    d.AppendLine(string.Format("   Attack Shark {0:X4}/{1:X4}: {2}", c.UsagePage, c.Usage,
                        ok ? string.Format("{0}{1} ({2} mV)", n < 0 ? "--" : n + "%", carr ? " carregando" : "", mv) : estado == "dormindo" ? "mouse dormindo ou desligado" : estado));
                    if (!ok) { if (estado == "dormindo") lido = true; continue; }
                    // tipo fixo: a colecao de configuracao usa a pagina 0x0B e enganaria o TipoPelaColecao
                    var a = new Aparelho { Id = "attackshark:" + c0.Vid.ToString("X4"), Nome = "Attack Shark X3", Tipo = Tipo.Mouse, Fonte = "Attack Shark" };
                    a.Valores.Add(new Valor { Rotulo = "", Nivel = n, Carregando = carr });
                    res.Aparelhos.Add(a); lido = true; break;
                }
            }

            string vp = string.Format("{0:X4}:{1:X4}", c0.Vid, c0.Pid);
            res.Vp = vp; res.Lido = lido;
            if (!lido && SEM_FIO.IsMatch(produto + " " + fab))
            {
                res.SemLeitura = string.Format("{0} ({1})", (fab + " " + produto).Trim(), vp);
                d.AppendLine("   sem fio, sem leitura ainda");
            }
        }
    }

    // diagnostico escrito por uma linha que pode ser abandonada no meio
    class Linhas
    {
        readonly System.Text.StringBuilder sb;
        public Linhas(System.Text.StringBuilder sb) { this.sb = sb; }
        public void AppendLine(string s) { lock (sb) sb.AppendLine(s); }
    }

    static Tipo TipoPelaColecao(List<Hid.Colecao> cols)
    {
        if (cols.Any(c => c.UsagePage == 0x01 && c.Usage == 0x02)) return Tipo.Mouse;
        if (cols.Any(c => c.UsagePage == 0x01 && c.Usage == 0x06)) return Tipo.Teclado;
        if (cols.Any(c => c.UsagePage == 0x01 && (c.Usage == 0x04 || c.Usage == 0x05))) return Tipo.Controle;
        if (cols.Any(c => c.UsagePage == 0x0B)) return Tipo.Fone;     // telefonia: headsets
        return Tipo.Outro;
    }
}

// ================================================================ juncao das fontes
class Coletor
{
    public List<Aparelho> Aparelhos = new List<Aparelho>();
    public DateTime Atualizado;
    public event Action Mudou;
    readonly FastPair fp = new FastPair();
    readonly Control ui;
    List<Windows_.Grupo> grupos = new List<Windows_.Grupo>();
    readonly Dictionary<ulong, int> gattCache = new Dictionary<ulong, int>();
    int ciclo;
    Receptores.Resultado usb = new Receptores.Resultado();
    public List<string> SemLeitura { get { return usb.SemLeitura; } }
    public string FastPairErro;

    // texto para "Copiar diagnostico": tudo o que cada fonte achou, sem nada gravado
    public string Diagnostico()
    {
        var d = new System.Text.StringBuilder();
        d.AppendLine("Juicebar " + Atualizador.Atual + " - diagnóstico de " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
        d.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitProcess ? " 64 bits" : " 32 bits"));
        d.AppendLine();
        d.AppendLine("== Bluetooth (Windows)");
        try
        {
            foreach (var g in Windows_.Ler())
                d.AppendLine(string.Format("{0} | {1} | bateria {2} | {3}{4}", g.Nome, g.Tipo, g.Bat < 0 ? "-" : g.Bat + "%", g.Conectado ? "conectado" : "desconectado", g.EnderecoLE != 0 ? " | LE" : ""));
        }
        catch (Exception ex) { d.AppendLine("erro: " + ex.Message); }
        d.AppendLine();
        d.AppendLine("== Fast Pair (anúncios dos últimos 10 min)");
        if (FastPairErro != null) d.AppendLine("indisponível: " + FastPairErro);
        foreach (var f in fp.Recentes()) d.AppendLine(f.Nome + " | " + BitConverter.ToString(f.Bat));
        d.AppendLine();
        d.AppendLine("== USB e receptores 2.4 GHz");
        try { d.Append(Receptores.Ler().Diagnostico); } catch (Exception ex) { d.AppendLine("erro: " + ex.Message); }
        return d.ToString();
    }

    public Coletor(Control ui)
    {
        this.ui = ui;
        fp.Mudou += () => Postar(Montar);
    }

    // --demo: aparelhos de exemplo, sem ler nada (para ver o visual e tirar a foto do README)
    public static bool Demo;

    void MontarDemo()
    {
        var fone = new Aparelho { Id = "demo-fone", Nome = "Fone Bluetooth", Tipo = Tipo.Fone, Fonte = "Fast Pair" };
        fone.Valores.Add(new Valor { Rotulo = "E", Nivel = 90 });
        fone.Valores.Add(new Valor { Rotulo = "D", Nivel = 35 });
        fone.Valores.Add(new Valor { Rotulo = "Estojo", Nivel = 60, Carregando = true, NaBarra = false });
        var mouse = new Aparelho { Id = "demo-mouse", Nome = "Mouse Bluetooth", Tipo = Tipo.Mouse, Fonte = "Windows" };
        mouse.Valores.Add(new Valor { Rotulo = "", Nivel = 72 });
        var teclado = new Aparelho { Id = "demo-teclado", Nome = "Teclado Bluetooth", Tipo = Tipo.Teclado, Fonte = "Windows" };
        teclado.Valores.Add(new Valor { Rotulo = "", Nivel = 15 });
        Aparelhos = new List<Aparelho> { fone, mouse, teclado };
        Atualizado = DateTime.Now;
        if (Mudou != null) Mudou();
    }

    public string Iniciar()
    {
        if (Demo) { Postar(MontarDemo); return null; }
        CarregarPiores();
        var erro = fp.Iniciar();
        FastPairErro = erro;
        var t = new Thread(Laco) { IsBackground = true, Name = "coletor" };
        t.Start();
        return erro;
    }

    public void Parar() { fp.Parar(); }

    void Laco()
    {
        while (true)
        {
            try
            {
                var g = Windows_.Ler();
                bool lerGatt = ciclo++ % 6 == 0;                   // LE: a cada ~2 min
                foreach (var x in g.Where(x => x.Bat < 0 && x.EnderecoLE != 0))
                {
                    if (lerGatt || !gattCache.ContainsKey(x.EnderecoLE)) gattCache[x.EnderecoLE] = Gatt.Ler(x.EnderecoLE);
                    x.Bat = gattCache[x.EnderecoLE];
                }
                Receptores.Resultado u = null;
                if (ciclo % 3 == 1)                                 // USB/2.4 GHz: a cada ~1 min, nunca prende o Bluetooth
                    Receptores.ComPrazo(() => { u = Receptores.Ler(); }, 90000);
                Postar(() => { grupos = g; if (u != null) usb = Manter(usb, u); Montar(); });
            }
            catch { }
            Thread.Sleep(20000);
        }
    }

    // aparelho no receptor que cochilou e nao respondeu: fica com a ultima leitura por ate 5 min
    static Receptores.Resultado Manter(Receptores.Resultado velho, Receptores.Resultado novo)
    {
        bool gravar = false;
        foreach (var a in novo.Aparelhos) { a.Visto = DateTime.Now; gravar |= Pior(piores, a, DateTime.Now); }
        if (gravar) GravarPiores();
        foreach (var a in velho.Aparelhos)
            if (!novo.Aparelhos.Any(x => x.Id == a.Id) && (DateTime.Now - a.Visto).TotalMinutes < 5) novo.Aparelhos.Add(a);
        return novo;
    }

    // bateria por faixa (ex.: K270 com pilha fraca): parado diz 70%, digitando cai para 20% (a tensao cede
    // com o radio ligado). Fica a menor leitura das ultimas 12 h, que e a que diz quando a pilha acaba.
    // Gravada em arquivo para valer depois de reiniciar; pilha trocada: 12 h sem a leitura baixa e vale a nova.
    public const double JANELA_FAIXA_H = 12;
    static readonly Dictionary<string, Tuple<int, DateTime>> piores = new Dictionary<string, Tuple<int, DateTime>>();
    static string ArquivoPiores { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Juicebar", "faixas.txt"); } }

    // devolve true se a memoria mudou (para gravar)
    public static bool Pior(Dictionary<string, Tuple<int, DateTime>> mem, Aparelho novo, DateTime agora)
    {
        if (!novo.PorFaixa || novo.Valores.Count == 0) return false;
        var vn = novo.Valores[0];
        if (vn.Nivel < 0 || vn.Carregando) return false;
        Tuple<int, DateTime> p;
        if (mem.TryGetValue(novo.Id, out p) && p.Item1 < vn.Nivel && (agora - p.Item2).TotalHours < JANELA_FAIXA_H)
        {
            vn.Nivel = p.Item1;
            return false;
        }
        mem[novo.Id] = Tuple.Create(vn.Nivel, agora);        // leitura igual ou mais baixa (ou janela vencida) renova
        return true;
    }

    public bool TemPiores { get { return piores.Count > 0; } }

    // menu "Pilhas trocadas": esquece a menor leitura guardada e le os receptores na hora
    public void ZerarPiores()
    {
        piores.Clear();
        try { File.Delete(ArquivoPiores); } catch { }
        new Thread(() =>
        {
            Receptores.Resultado u = null;
            try { u = Receptores.Ler(); } catch { }
            if (u != null) Postar(() => { usb = Manter(usb, u); Montar(); });
        }) { IsBackground = true }.Start();
    }

    static void CarregarPiores()
    {
        try
        {
            if (!File.Exists(ArquivoPiores)) return;
            foreach (var l in File.ReadAllLines(ArquivoPiores))
            {
                var c = l.Split('\t');
                int n; long t;
                if (c.Length == 3 && int.TryParse(c[1], out n) && long.TryParse(c[2], out t)) piores[c[0]] = Tuple.Create(n, new DateTime(t));
            }
        }
        catch { }
    }

    static void GravarPiores()
    {
        try { File.WriteAllLines(ArquivoPiores, piores.Select(kv => kv.Key + "\t" + kv.Value.Item1 + "\t" + kv.Value.Item2.Ticks)); }
        catch { }
    }

    void Postar(Action a)
    {
        try { if (ui.IsHandleCreated) ui.BeginInvoke(a); } catch { }
    }

    void Montar()
    {
        var lista = new List<Aparelho>();
        var fps = fp.Recentes();
        foreach (var g in grupos.Where(x => x.Conectado && x.Bat >= 0))
        {
            var a = new Aparelho { Id = g.Cont.ToString(), Nome = Limpar(g.Nome), Tipo = g.Tipo, Fonte = "Windows" };
            string n = FastPair.Normal(g.Nome);
            var casado = n.Length >= 4 ? fps.FirstOrDefault(f => { var m = FastPair.Normal(f.Nome); return m == n || m.StartsWith(n) || n.StartsWith(m); }) : null;
            if (casado != null)
            {
                a.Fonte = "Fast Pair";
                a.Tipo = Tipo.Fone;
                var lados = Lados(a.Id, casado.Bat);
                if (lados[0] == 0x7F && lados[1] == 0x7F) a.Valores.Add(new Valor { Rotulo = "", Nivel = g.Bat });
                else
                {
                    if (lados[0] != 0x7F) a.Valores.Add(V("E", lados[0], true));
                    if (lados[1] != 0x7F) a.Valores.Add(V("D", lados[1], true));
                }
                if (lados[2] != 0x7F) a.Valores.Add(V("Estojo", lados[2], false));
            }
            else a.Valores.Add(new Valor { Rotulo = "", Nivel = g.Bat });
            lista.Add(a);
        }
        lista.AddRange(usb.Aparelhos);
        Aparelhos = lista.OrderBy(x => x.Tipo).ThenBy(x => x.Nome).ToList();
        Atualizado = DateTime.Now;
        if (Mudou != null) Mudou();
    }

    // O Fast Pair as vezes anuncia um lado como desconhecido (0x7F): no estojo, longe ou entre anuncios.
    // Fica o ultimo valor conhecido de cada lado por 10 min; sem nenhum, o lado nao aparece.
    readonly Dictionary<string, Tuple<byte, DateTime>> ultimoLado = new Dictionary<string, Tuple<byte, DateTime>>();

    byte[] Lados(string id, byte[] bat)
    {
        var r = new byte[3];
        for (int k = 0; k < 3; k++) r[k] = Lado(id + "|" + k, bat[k]);
        return r;
    }

    byte Lado(string chave, byte b)
    {
        if ((b & 0x7F) <= 100) { ultimoLado[chave] = Tuple.Create(b, DateTime.Now); return b; }
        Tuple<byte, DateTime> u;
        if (ultimoLado.TryGetValue(chave, out u) && (DateTime.Now - u.Item2).TotalMinutes < 10) return u.Item1;
        return 0x7F;
    }

    static Valor V(string r, byte b, bool naBarra)
    {
        int n = b & 0x7F;
        return new Valor { Rotulo = r, Nivel = n > 100 ? -1 : n, Carregando = (b & 0x80) != 0 && n <= 100, NaBarra = naBarra };
    }

    static string Limpar(string s)
    {
        s = s ?? "";
        foreach (var suf in new[] { " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free", " Stereo", " Avrcp Transport" })
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - suf.Length);
        return s.Trim();
    }
}

// ================================================================ desenho comum
static class Visual
{
    public static Color Cor(int n)
    {
        if (n < 0) return Color.FromArgb(140, 140, 140);
        if (n <= 20) return Color.FromArgb(255, 99, 88);
        if (n <= 40) return Color.FromArgb(255, 185, 64);
        return Color.FromArgb(255, 255, 255);
    }

    public static string Glifo(Tipo t)
    {
        switch (t)
        {
            case Tipo.Fone: return "";
            case Tipo.Mouse: return "";
            case Tipo.Teclado: return "";
            case Tipo.Controle: return "";
            default: return "";
        }
    }

    // pilha pequena: contorno, polo e preenchimento proporcional
    public static void Pilha(Graphics g, float x, float y, float w, float h, int nivel, bool carregando, Color contorno)
    {
        using (var p = new Pen(contorno, 1f))
        using (var path = Arredondado(new RectangleF(x, y, w, h), 2f))
        {
            g.DrawPath(p, path);
            using (var polo = new SolidBrush(contorno)) g.FillRectangle(polo, x + w + 0.5f, y + h * 0.3f, 1.6f, h * 0.4f);
            if (nivel > 0)
            {
                float iw = (w - 3f) * Math.Min(nivel, 100) / 100f;
                var cor = carregando ? Color.FromArgb(110, 220, 130) : Cor(nivel);
                using (var b = new SolidBrush(cor))
                using (var ip = Arredondado(new RectangleF(x + 1.5f, y + 1.5f, Math.Max(iw, 1.5f), h - 3f), 1f))
                    g.FillPath(b, ip);
            }
        }
    }

    static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var p = new GraphicsPath(); float d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static string Texto(Valor v) { return v.Nivel < 0 ? "--" : v.Nivel + "%"; }
}

// ================================================================ barra de tarefas
class Widget : Form
{
    readonly Coletor col;
    readonly Detalhes detalhes;
    readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer();
    readonly ToolTip dica = new ToolTip();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly System.Windows.Forms.Timer checagem = new System.Windows.Forms.Timer();
    bool procurando;
    IntPtr barra = IntPtr.Zero;
    Rectangle onde;
    int largura;
    bool sobre;

    public Widget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-3000, -3000);
        Size = new Size(10, 40);
        Text = "Juicebar (barra)";
        col = new Coletor(this);
        detalhes = new Detalhes(col);
        col.Mudou += () => { Encaixar(); Desenhar(); AtualizarDica(); };

        menu.Items.Add("Detalhes", null, (s, e) => AbrirDetalhes());
        var atualizar = new ToolStripMenuItem("Procurar atualizações");
        atualizar.Click += (s, e) => ProcurarAtualizacao(true);
        menu.Items.Add(atualizar);
        menu.Items.Add("Copiar diagnóstico", null, (s, e) => CopiarDiagnostico());
        var auto = new ToolStripMenuItem("Iniciar com o Windows");
        auto.Click += (s, e) => { Inicio.Ligar(!Inicio.Ligado()); };
        menu.Items.Add(auto);
        var pilhas = new ToolStripMenuItem("Troquei as pilhas (zerar menor leitura)");
        pilhas.ToolTipText = "Aparelhos que informam a bateria por faixa (ex.: teclado Logitech K270) mostram a menor leitura das últimas 12 h. Use depois de trocar as pilhas.";
        pilhas.Click += (s, e) => col.ZerarPiores();
        menu.Items.Add(pilhas);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (s, e) => { col.Parar(); Application.Exit(); });
        menu.Opening += (s, e) =>
        {
            auto.Checked = Inicio.Ligado();
            pilhas.Visible = col.TemPiores;
            var r = Atualizador.Disponivel;
            atualizar.Text = r == null ? "Procurar atualizações" : "Atualizar para a versão " + r.Tag.TrimStart('v');
            atualizar.Font = new Font(menu.Font, r == null ? FontStyle.Regular : FontStyle.Bold);
            atualizar.Image = r == null ? null : PontoAmarelo(12);
        };
        dica.InitialDelay = 300;

        // procura versao nova 20 s depois de abrir e depois a cada 6 h, sem avisar se nao tiver
        checagem.Interval = 20000;
        checagem.Tick += (s, e) => { checagem.Interval = 6 * 3600 * 1000; ProcurarAtualizacao(false); };

        // clique direito vem pelo WM_CONTEXTMENU (ver WndProc), para a barra do Windows nao abrir o menu dela
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) AbrirDetalhes(); };
        MouseEnter += (s, e) => { sobre = true; Desenhar(); };
        MouseLeave += (s, e) => { sobre = false; Desenhar(); };

        relogio.Interval = 1500;
        relogio.Tick += (s, e) => { Encaixar(); };
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080; return cp; }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    // A janela e filha da barra de tarefas: o WM_CONTEXTMENU que nao for tratado aqui sobe para
    // a barra e o Windows abre o menu dele por cima do nosso. Trata e nao repassa.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x007B)                                  // WM_CONTEXTMENU
        {
            N.SetForegroundWindow(Handle);
            menu.Show(Cursor.Position);
            return;
        }
        base.WndProc(ref m);
    }

    // aviso sempre na frente (a barra nao e uma janela normal para ser dona de caixa de mensagem)
    static DialogResult Aviso(string texto, MessageBoxButtons botoes, MessageBoxIcon icone)
    {
        using (var dono = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Size = new Size(1, 1), StartPosition = FormStartPosition.CenterScreen, Opacity = 0 })
        {
            dono.Show();
            dono.Activate();
            return MessageBox.Show(dono, texto, "Juicebar", botoes, icone);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var erro = col.Iniciar();
        Encaixar(); Desenhar(); AtualizarDica();
        if (erro != null) dica.SetToolTip(this, "Bluetooth indisponível: " + erro);
        relogio.Start();
        if (!Coletor.Demo) checagem.Start();
    }

    // manual = clicou no menu: mostra o resultado e oferece instalar; automatico: so acende o ponto
    void ProcurarAtualizacao(bool manual)
    {
        if (procurando) return;
        var r0 = Atualizador.Disponivel;
        if (manual && r0 != null) { OferecerInstalar(r0); return; }
        procurando = true;
        new Thread(() =>
        {
            string erro;
            var r = Atualizador.Procurar(out erro);
            try
            {
                BeginInvoke(new Action(() =>
                {
                    procurando = false;
                    if (erro == null) { Atualizador.Disponivel = r; Desenhar(); AtualizarDica(); }
                    if (!manual) return;
                    if (erro != null) Aviso("Não consegui procurar atualizações agora.\n\n" + erro, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else if (r == null) Aviso("Você já está na versão mais nova (" + Atualizador.Atual + ").", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else OferecerInstalar(r);
                }));
            }
            catch { procurando = false; }
        }) { IsBackground = true }.Start();
    }

    void OferecerInstalar(Atualizador.Release r)
    {
        string txt = "Tem uma versão nova do Juicebar: " + r.Tag.TrimStart('v') + (r.Beta ? " (beta)" : "") + ".\nVocê está na " + Atualizador.Atual + ".\n\nAtualizar agora? O Juicebar fecha e abre de novo sozinho.";
        var resp = Aviso(txt, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (resp != DialogResult.Yes) return;
        Cursor = Cursors.WaitCursor;
        var erro = Atualizador.Instalar(r);
        Cursor = Cursors.Default;
        if (erro == null) { col.Parar(); Application.Exit(); return; }
        if (Aviso("Não consegui atualizar sozinho: " + erro + "\n\nAbrir a página da versão nova para baixar?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes && r.Pagina != null)
            try { Process.Start(r.Pagina); } catch { }
    }

    bool diagnosticando;

    // monta o diagnostico fora da tela (os receptores podem demorar) e copia quando termina
    void CopiarDiagnostico()
    {
        if (diagnosticando) return;
        diagnosticando = true;
        new Thread(() =>
        {
            string txt;
            try { txt = col.Diagnostico(); } catch (Exception ex) { txt = "erro no diagnóstico: " + ex.Message; }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    diagnosticando = false;
                    bool copiou = false;
                    for (int i = 0; i < 5 && !copiou; i++)              // area de transferencia ocupada: tenta de novo
                    {
                        try { Clipboard.SetDataObject(txt, true, 3, 100); copiou = true; } catch { Thread.Sleep(150); }
                    }
                    if (copiou)
                        Aviso("O diagnóstico foi copiado. Ele lista os aparelhos e receptores que o Juicebar achou e o que cada um respondeu.\n\nSe algum aparelho sem fio não aparece na barra, abra uma issue em github.com/caiquebmaso/juicebar/issues e cole o texto (Ctrl+V).", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                    {
                        var arq = Path.Combine(Path.GetTempPath(), "juicebar-diagnostico.txt");
                        try { File.WriteAllText(arq, txt, System.Text.Encoding.UTF8); Process.Start("notepad.exe", "\"" + arq + "\""); } catch { }
                        Aviso("Não consegui copiar para a área de transferência. O diagnóstico abriu no Bloco de Notas.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }));
            }
            catch { diagnosticando = false; }
        }) { IsBackground = true, Name = "diagnostico" }.Start();
    }

    public static Bitmap PontoAmarelo(int t)
    {
        var b = new Bitmap(t, t);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Color.FromArgb(255, 200, 0))) g.FillEllipse(br, 1, 1, t - 3, t - 3);
        }
        return b;
    }

    void AbrirDetalhes()
    {
        detalhes.Show();
        if (detalhes.WindowState == FormWindowState.Minimized) detalhes.WindowState = FormWindowState.Normal;
        detalhes.Activate();
    }

    // largura de cada aparelho na barra
    static int LarguraDe(Aparelho a) { return a.Valores.Count(v => v.NaBarra) > 1 ? 94 : 72; }   // inclui 6 px de folga entre aparelhos

    int LarguraTotal()
    {
        if (col.Aparelhos.Count == 0) return 30;
        return col.Aparelhos.Sum(a => LarguraDe(a)) + 4;
    }

    // Filho da barra de tarefas, logo a esquerda do clima (ou da bandeja); encolhe a area
    // dos botoes para abrir espaco. Refaz sozinho se o Explorer reiniciar: a barra velha leva a
    // nossa janela junto quando morre, entao o relogio (que nao depende da janela) cria outra
    // assim que a barra nova aparece.
    void Encaixar()
    {
        if (IsDisposed) return;
        IntPtr tray = N.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        // barra recem-criada pelo Explorer ainda sem tamanho: espera o proximo tique
        // (desenhar com altura 0 dava "Parametro invalido" no Bitmap)
        N.RECT rt; N.GetWindowRect(tray, out rt);
        if (rt.Right - rt.Left <= 0 || rt.Bottom - rt.Top <= 0 || !N.IsWindowVisible(tray)) return;
        if (!IsHandleCreated) { CreateHandle(); barra = IntPtr.Zero; }
        if (tray != barra || N.GetParent(Handle) != tray)
        {
            N.SetParent(Handle, tray);
            long st = N.GetWindowLongPtr(Handle, -16).ToInt64();
            st = (st & ~0x80000000L) | 0x40000000L | 0x10000000L;
            N.SetWindowLongPtr(Handle, -16, new IntPtr(st));
            barra = tray;
            onde = Rectangle.Empty;
        }

        int ancora = int.MaxValue;
        foreach (var cls in new[] { "DynamicContent2", "TrayNotifyWnd" })
        {
            IntPtr h = N.FindWindowEx(tray, IntPtr.Zero, cls, null);
            if (h == IntPtr.Zero || !N.IsWindowVisible(h)) continue;
            N.RECT r; N.GetWindowRect(h, out r);
            if (r.Right > r.Left) ancora = Math.Min(ancora, r.Left);
        }
        if (ancora == int.MaxValue) ancora = rt.Right - 300;

        largura = LarguraTotal();
        int alt = rt.Bottom - rt.Top;
        int x = ancora - largura - 4 - rt.Left;
        var novo = new Rectangle(x, 0, largura, alt);
        if (novo != onde)
        {
            N.SetWindowPos(Handle, IntPtr.Zero, novo.X, novo.Y, novo.Width, novo.Height, 0x0010 | 0x0004 | 0x0040);
            onde = novo;
            Desenhar();
        }

        IntPtr rebar = N.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar != IntPtr.Zero)
        {
            N.RECT rr; N.GetWindowRect(rebar, out rr);
            int limite = rt.Left + x - 2;
            if (rr.Right > limite)
                N.SetWindowPos(rebar, IntPtr.Zero, rr.Left - rt.Left, rr.Top - rt.Top, limite - rr.Left, rr.Bottom - rr.Top, 0x0010 | 0x0004);
        }
    }

    void Desenhar()
    {
        if (onde.Width <= 0 || onde.Height <= 0) return;
        int W = onde.Width, H = onde.Height;
        using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(1, 0, 0, 0));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (sobre) using (var hb = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillRectangle(hb, 0, 0, W, H);

                using (var fIcone = new Font("Segoe MDL2 Assets", 16f, GraphicsUnit.Pixel))
                using (var fGrande = new Font("Segoe UI", 12.5f, GraphicsUnit.Pixel))
                using (var fPeq = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel))
                using (var fRot = new Font("Segoe UI Semibold", 10f, GraphicsUnit.Pixel))
                {
                    if (col.Aparelhos.Count == 0)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(120, 120, 120)))
                        {
                            var t = g.MeasureString("", fIcone);
                            g.DrawString("", fIcone, b, (W - t.Width) / 2f, (H - t.Height) / 2f);
                        }
                    }
                    float x = 2;
                    foreach (var a in col.Aparelhos)
                    {
                        int w = LarguraDe(a);
                        Bloco(g, a, x, w, H, fIcone, fGrande, fPeq, fRot);
                        x += w;
                    }
                    if (Atualizador.Disponivel != null)            // versao nova: ponto amarelo no canto
                    {
                        using (var borda = new SolidBrush(Color.FromArgb(200, 30, 30, 30))) g.FillEllipse(borda, W - 11, 3, 9, 9);
                        using (var am = new SolidBrush(Color.FromArgb(255, 200, 0))) g.FillEllipse(am, W - 10, 4, 7, 7);
                    }
                }
            }
            Aplicar(bmp);
        }
    }

    static void Bloco(Graphics g, Aparelho a, float x, int w, int H, Font fIcone, Font fGrande, Font fPeq, Font fRot)
    {
        var naBarra = a.Valores.Where(v => v.NaBarra).ToList();
        using (var bIcone = new SolidBrush(Visual.Cor(a.Menor) == Color.FromArgb(255, 255, 255) ? Color.FromArgb(230, 230, 230) : Visual.Cor(a.Menor)))
        {
            var t = g.MeasureString(Visual.Glifo(a.Tipo), fIcone);
            g.DrawString(Visual.Glifo(a.Tipo), fIcone, bIcone, x + 2, (H - t.Height) / 2f + 1);
        }
        var contorno = Color.FromArgb(200, 200, 200, 200);
        float cx = x + 24;
        if (naBarra.Count == 1)
        {
            var v = naBarra[0];
            Visual.Pilha(g, cx, H / 2f - 4.5f, 15, 9, v.Nivel, v.Carregando, contorno);
            using (var b = new SolidBrush(Visual.Cor(v.Nivel)))
            {
                var t = g.MeasureString(Visual.Texto(v), fGrande);
                g.DrawString(Visual.Texto(v), fGrande, b, cx + 19, (H - t.Height) / 2f);
            }
        }
        else
        {
            for (int i = 0; i < Math.Min(2, naBarra.Count); i++)
            {
                var v = naBarra[i];
                float cy = H / 2f + (i == 0 ? -8.5f : 8.5f);
                using (var cinza = new SolidBrush(Color.FromArgb(175, 175, 175)))
                {
                    var tr = g.MeasureString(v.Rotulo, fRot);
                    g.DrawString(v.Rotulo, fRot, cinza, cx, cy - tr.Height / 2f);
                }
                Visual.Pilha(g, cx + 13, cy - 3.5f, 13, 7, v.Nivel, v.Carregando, contorno);
                using (var b = new SolidBrush(Visual.Cor(v.Nivel)))
                {
                    var t = g.MeasureString(Visual.Texto(v), fPeq);
                    g.DrawString(Visual.Texto(v), fPeq, b, cx + 30, cy - t.Height / 2f);
                }
            }
        }
    }

    void Aplicar(Bitmap bmp)
    {
        IntPtr tela = N.GetDC(IntPtr.Zero), mem = N.CreateCompatibleDC(tela);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), velho = N.SelectObject(mem, hbmp);
        try
        {
            var tam = new N.SIZE { cx = bmp.Width, cy = bmp.Height };
            var orig = new N.POINT();
            var bl = new N.BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            N.UpdateLayeredWindow(Handle, tela, IntPtr.Zero, ref tam, mem, ref orig, 0, ref bl, 2);
        }
        finally { N.SelectObject(mem, velho); N.DeleteObject(hbmp); N.DeleteDC(mem); N.ReleaseDC(IntPtr.Zero, tela); }
    }

    void AtualizarDica()
    {
        string aviso = Atualizador.Disponivel == null ? "" : "Versão nova disponível: " + Atualizador.Disponivel.Tag.TrimStart('v') + " (clique direito > Atualizar)\n\n";
        if (col.Aparelhos.Count == 0) { dica.SetToolTip(this, aviso + "Nenhum aparelho sem fio com bateria conectado"); return; }
        var linhas = new List<string>();
        if (aviso.Length > 0) linhas.Add(aviso.TrimEnd('\n') + "\n");
        foreach (var a in col.Aparelhos)
        {
            linhas.Add(a.Nome);
            foreach (var v in a.Valores)
            {
                string r = v.Rotulo == "E" ? "Esquerdo" : v.Rotulo == "D" ? "Direito" : v.Rotulo == "" ? "Bateria" : v.Rotulo;
                linhas.Add("   " + r + ": " + (v.Nivel < 0 ? "sem informação" : v.Nivel + "%") + (v.Carregando ? " (carregando)" : ""));
            }
        }
        linhas.Add("Atualizado às " + col.Atualizado.ToString("HH:mm:ss"));
        dica.SetToolTip(this, string.Join("\n", linhas));
    }
}

// ================================================================ janela de detalhes
class Detalhes : Form
{
    readonly Coletor col;
    readonly Panel corpo = new Panel();
    readonly Label rodape = new Label();

    public Detalhes(Coletor col)
    {
        this.col = col;
        Text = "Juicebar";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 200);
        corpo.SetBounds(0, 0, 400, 170);
        corpo.Paint += Pintar;
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(corpo, true, null);
        Controls.Add(corpo);
        rodape.ForeColor = Color.FromArgb(150, 150, 150);
        rodape.Font = new Font("Segoe UI", 9f);
        Controls.Add(rodape);
        col.Mudou += () => { if (Visible) Refazer(); };
        VisibleChanged += (s, e) => { if (Visible) Refazer(); };
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sim = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref sim, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref sim, 4);   // barra de titulo escura
    }

    void Refazer()
    {
        int h = 20;
        foreach (var a in col.Aparelhos) h += 44 + a.Valores.Count * 34 + 10;
        if (col.Aparelhos.Count == 0) h = 80;
        h += AlturaSemLeitura();
        ClientSize = new Size(400, h + 34);
        corpo.SetBounds(0, 0, 400, h);
        rodape.SetBounds(20, h + 6, 360, 22);
        rodape.Text = "Atualizado às " + col.Atualizado.ToString("HH:mm:ss");
        corpo.Invalidate();
    }

    void Pintar(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var fIcone = new Font("Segoe MDL2 Assets", 20f, GraphicsUnit.Pixel))
        using (var fNome = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
        using (var fTxt = new Font("Segoe UI", 13.5f, GraphicsUnit.Pixel))
        using (var fNum = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
        using (var fPeq = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel))
        using (var branco = new SolidBrush(Color.White))
        using (var cinza = new SolidBrush(Color.FromArgb(160, 160, 160)))
        using (var trilho = new SolidBrush(Color.FromArgb(60, 60, 60)))
        {
            if (col.Aparelhos.Count == 0)
            {
                g.DrawString("Nenhum aparelho sem fio com bateria conectado.", fTxt, cinza, 20, 28);
                SemLeitura(g, 80, fNome, fPeq, branco, cinza);
                return;
            }
            float y = 20;
            foreach (var a in col.Aparelhos)
            {
                g.DrawString(Visual.Glifo(a.Tipo), fIcone, branco, 18, y + 2);
                g.DrawString(a.Nome, fNome, branco, 50, y);
                g.DrawString(a.Fonte == "Fast Pair" ? "cada lado pelo Fast Pair" : a.Fonte == "Logitech" ? "pelo receptor Logitech" : a.Fonte == "USB" ? "pelo padrão USB de bateria" : a.Fonte == "Razer" ? "pelo receptor Razer" : "pelo Windows", fPeq, cinza, 50, y + 20);
                y += 44;
                foreach (var v in a.Valores)
                {
                    string r = v.Rotulo == "E" ? "Esquerdo" : v.Rotulo == "D" ? "Direito" : v.Rotulo == "" ? "Bateria" : v.Rotulo;
                    g.DrawString(r, fTxt, branco, 50, y);
                    g.FillRectangle(trilho, 140, y + 7, 170, 8);
                    if (v.Nivel >= 0)
                        using (var b = new SolidBrush(v.Carregando ? Color.FromArgb(110, 220, 130) : (v.Nivel > 40 ? Color.FromArgb(90, 200, 120) : Visual.Cor(v.Nivel))))
                            g.FillRectangle(b, 140, y + 7, 170f * v.Nivel / 100f, 8);
                    string t = Visual.Texto(v);
                    var sz = g.MeasureString(t, fNum);
                    g.DrawString(t, fNum, branco, 380 - sz.Width, y - 1);
                    if (v.Carregando) g.DrawString("carregando", fPeq, cinza, 140, y + 16);
                    else if (v.Nivel < 0) g.DrawString("sem informação (estojo fechado ou longe)", fPeq, cinza, 140, y + 16);
                    y += 34;
                }
                y += 10;
            }
            SemLeitura(g, y, fNome, fPeq, branco, cinza);
        }
    }

    // receptores sem fio que o Juicebar reconheceu mas ainda nao sabe ler
    void SemLeitura(Graphics g, float y, Font fNome, Font fPeq, Brush branco, Brush cinza)
    {
        if (col.SemLeitura.Count == 0) return;
        g.DrawString("Sem leitura ainda", fNome, branco, 20, y);
        y += 24;
        foreach (var s in col.SemLeitura) { g.DrawString(s, fPeq, cinza, 20, y); y += 18; }
        g.DrawString("Clique direito > Copiar diagnóstico e abra uma issue no GitHub.", fPeq, cinza, 20, y + 4);
    }

    int AlturaSemLeitura() { return col.SemLeitura.Count == 0 ? 0 : 24 + col.SemLeitura.Count * 18 + 30; }
}

// ================================================================ Win32
static class N
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string t);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint f);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pDst, ref SIZE sz, IntPtr src, ref POINT pSrc, int key, ref BLEND bl, int flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}

// ================================================================ atualizacoes (releases do GitHub)
// Versao estavel so ve versao estavel; versao beta ve as duas.
static class Versao
{
    public static int[] Numeros(string v, out string pre)
    {
        v = (v ?? "").Trim().TrimStart('v', 'V');
        pre = "";
        int h = v.IndexOf('-');
        if (h >= 0) { pre = v.Substring(h + 1); v = v.Substring(0, h); }
        var p = v.Split('.');
        var n = new int[3];
        for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], out n[i]);
        return n;
    }

    public static bool Beta(string v) { string pre; Numeros(v, out pre); return pre.Length > 0; }

    // >0 se a for mais nova que b
    public static int Comparar(string a, string b)
    {
        string pa, pb;
        var na = Numeros(a, out pa); var nb = Numeros(b, out pb);
        for (int i = 0; i < 3; i++) if (na[i] != nb[i]) return na[i].CompareTo(nb[i]);
        if (pa.Length == 0 && pb.Length == 0) return 0;
        if (pa.Length == 0) return 1;                 // 1.1.0 e mais nova que 1.1.0-beta
        if (pb.Length == 0) return -1;
        return string.CompareOrdinal(pa, pb);
    }
}

static class Atualizador
{
    const string REPO = "caiquebmaso/juicebar";
    public static string Atual = ((System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
        System.Reflection.Assembly.GetExecutingAssembly(), typeof(System.Reflection.AssemblyInformationalVersionAttribute))).InformationalVersion;

    public class Release { public string Tag, Pagina, Exe; public bool Beta; }
    public static Release Disponivel;          // != null: tem versao nova (ponto amarelo na barra)

    public static Release Procurar(out string erro)
    {
        erro = null;
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;    // TLS 1.2
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Juicebar";
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                wc.Encoding = System.Text.Encoding.UTF8;
                json = wc.DownloadString("https://api.github.com/repos/" + REPO + "/releases?per_page=30");
            }
            var lista = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as object[];
            Release melhor = null;
            bool aceitaBeta = Versao.Beta(Atual);
            foreach (var o in lista ?? new object[0])
            {
                var r = o as Dictionary<string, object>;
                if (r == null || true.Equals(r["draft"])) continue;
                bool beta = true.Equals(r["prerelease"]);
                if (beta && !aceitaBeta) continue;
                string tag = r["tag_name"] as string;
                if (tag == null || Versao.Comparar(tag, Atual) <= 0) continue;
                if (melhor != null && Versao.Comparar(tag, melhor.Tag) <= 0) continue;
                string exe = null;
                foreach (var ao in (r["assets"] as object[]) ?? new object[0])
                {
                    var a = ao as Dictionary<string, object>;
                    if (a != null && string.Equals(a["name"] as string, "juicebar.exe", StringComparison.OrdinalIgnoreCase)) exe = a["browser_download_url"] as string;
                }
                melhor = new Release { Tag = tag, Pagina = r["html_url"] as string, Exe = exe, Beta = beta };
            }
            return melhor;
        }
        catch (Exception ex) { erro = ex.Message; return null; }
    }

    // baixa o .exe novo, troca pelo atual (o que esta rodando vira .velho) e abre o novo
    public static string Instalar(Release r)
    {
        if (r.Exe == null) return "a versão " + r.Tag + " não tem o juicebar.exe anexado";
        string exe = Application.ExecutablePath, novo = exe + ".novo", velho = exe + ".velho";
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Juicebar";
                wc.DownloadFile(r.Exe, novo);
            }
            var b = File.ReadAllBytes(novo);
            if (b.Length < 20000 || b[0] != 'M' || b[1] != 'Z') { File.Delete(novo); return "o arquivo baixado não é um programa válido"; }
            if (File.Exists(velho)) File.Delete(velho);
            File.Move(exe, velho);
            try { File.Move(novo, exe); }
            catch { File.Move(velho, exe); throw; }
            Process.Start(exe, "--esperar");
            return null;
        }
        catch (Exception ex) { try { if (File.Exists(novo)) File.Delete(novo); } catch { } return ex.Message; }
    }

    public static void LimparVelho()
    {
        try { var v = Application.ExecutablePath + ".velho"; if (File.Exists(v)) File.Delete(v); } catch { }
    }
}

// "Iniciar com o Windows": entrada Juicebar em HKCU\...\Run apontando para este .exe
static class Inicio
{
    const string CHAVE = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Exe { get { return "\"" + Application.ExecutablePath + "\""; } }

    public static bool Ligado()
    {
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CHAVE))
        {
            var v = k == null ? null : k.GetValue("Juicebar") as string;
            return v != null && string.Equals(v.Trim('"'), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Ligar(bool sim)
    {
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(CHAVE))
        {
            if (sim) k.SetValue("Juicebar", Exe);
            else if (k.GetValue("Juicebar") != null) k.DeleteValue("Juicebar");
        }
    }
}

// ================================================================ testes (--testar)
// Respostas montadas byte a byte pela documentacao de cada protocolo, sem aparelho conectado.
static class Testes
{
    static int falhas; static System.Text.StringBuilder log;

    static void Conferir(string nome, object esperado, object obtido)
    {
        bool ok = Equals(esperado, obtido);
        if (!ok) falhas++;
        log.AppendLine((ok ? "ok    " : "FALHA ") + nome + (ok ? "" : "  esperado " + esperado + ", veio " + obtido));
    }

    static void Logitech(string nome, int feature, byte[] resp, int nivel, bool carregando)
    {
        int n; bool c;
        bool ok = Logi.Decodificar(feature, resp, out n, out c);
        Conferir(nome, nivel + "|" + carregando, ok ? n + "|" + c : "sem leitura");
    }

    public static int Rodar(string arquivo)
    {
        falhas = 0; log = new System.Text.StringBuilder();
        log.AppendLine("Juicebar " + Atualizador.Atual + " - testes");

        // Logitech 0x1004 Unified Battery, funcao get_status: [%, faixa, estado, energia externa]
        Logitech("1004 85% descarregando", 0x1004, new byte[] { 85, 0x04, 0x00, 0x00 }, 85, false);
        Logitech("1004 40% carregando", 0x1004, new byte[] { 40, 0x02, 0x01, 0x01 }, 40, true);
        Logitech("1004 sem %, faixa boa", 0x1004, new byte[] { 0, 0x04, 0x00, 0x00 }, 60, false);
        Logitech("1004 sem %, faixa critica", 0x1004, new byte[] { 0, 0x01, 0x00, 0x00 }, 5, false);
        Logitech("1004 carga completa", 0x1004, new byte[] { 100, 0x08, 0x03, 0x01 }, 100, false);
        // Logitech 0x1000 Battery Status: [%, proximo nivel, estado]
        Logitech("1000 50% descarregando", 0x1000, new byte[] { 50, 20, 0x00 }, 50, false);
        Logitech("1000 70% carregando", 0x1000, new byte[] { 70, 50, 0x01 }, 70, true);
        Logitech("1000 carregado", 0x1000, new byte[] { 0, 0, 0x03 }, 100, false);
        // Logitech 0x1001 Battery Voltage: [mV alto, mV baixo, flags]
        Logitech("1001 4186 mV", 0x1001, new byte[] { 0x10, 0x5A, 0x00 }, 100, false);
        Logitech("1001 3811 mV", 0x1001, new byte[] { 0x0E, 0xE3, 0x00 }, 50, false);
        Logitech("1001 3900 mV carregando", 0x1001, new byte[] { 0x0F, 0x3C, 0x80 }, 67, true);
        int n0; bool c0;
        Conferir("1001 tensao absurda nao vira leitura", false, Logi.Decodificar(0x1001, new byte[] { 0x00, 0x10, 0x00 }, out n0, out c0));
        // 0x1F20 ADC (headsets)
        Logitech("1F20 3717 mV", 0x1F20, new byte[] { 0x0E, 0x85, 0x01 }, 20, false);

        // Razer: pedido de bateria (classe 0x07, comando 0x80) e resposta com 0xB3 (179/255 = 70%)
        var rz = Razer.Montar(0x1F, 0x07, 0x80, 0x02);
        Conferir("Razer pedido: transação, tamanho, classe, comando", "1F-02-07-80", string.Format("{0:X2}-{1:X2}-{2:X2}-{3:X2}", rz[2], rz[6], rz[7], rz[8]));
        Conferir("Razer pedido: CRC (sem estado e transação)", (byte)(0x02 ^ 0x07 ^ 0x80), rz[89]);
        var rr = new byte[91]; rr[1] = 0x02; rr[2] = 0x1F; rr[6] = 0x02; rr[7] = 0x07; rr[8] = 0x80; rr[10] = 0xB3;
        Conferir("Razer resposta 0xB3 = 70%", 70, Razer.PorcentoDe(Razer.Valor(rr, 0x07, 0x80)));
        rr[1] = 0x05;
        Conferir("Razer comando não suportado", -1, Razer.Valor(rr, 0x07, 0x80));

        // Attack Shark X3: aviso real do receptor 1D57:FA60 (03 10 40 01 08 = 4,00 V na bateria)
        int an, amv; bool ac;
        Conferir("Attack Shark aviso real 4,00 V = 70%", "70|False|4000", AttackShark.Decodificar(new byte[] { 0x03, 0x10, 0x40, 0x01, 0x08 }, out an, out ac, out amv) ? an + "|" + ac + "|" + amv : "sem leitura");
        Conferir("Attack Shark aviso vazio descartado", false, AttackShark.Decodificar(new byte[] { 0x03, 0x10, 0x03, 0x00, 0xFF }, out an, out ac, out amv));
        Conferir("Attack Shark cabo (5,00 V) = carregando sem %", "-1|True", AttackShark.Decodificar(new byte[] { 0x03, 0x10, 0x50, 0x00, 0x0C }, out an, out ac, out amv) ? an + "|" + ac : "sem leitura");
        Conferir("Attack Shark 4,20 V = 100%", 100, AttackShark.PorTensao(4.2));
        Conferir("Attack Shark 3,80 V = 35%", 35, AttackShark.PorTensao(3.8));

        // Compx: pedidos conferidos com o receptor 3554:F5D5 real (Redragon M916 Pro)
        Conferir("Compx pedido status", "08-03-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4A", BitConverter.ToString(Compx.Montar(Compx.STATUS)));
        Conferir("Compx pedido energia: checksum", (byte)0x49, Compx.Montar(Compx.ENERGIA)[16]);
        var cxs = new byte[] { 0x08, 0x03, 0, 0, 0, 0x01, 0x00, 0xA0, 0x1C, 0xEB, 0, 0, 0, 0, 0, 0, 0xA2 };   // resposta real, mouse dormindo
        Conferir("Compx resposta real do status valida", true, Compx.Valida(cxs, Compx.STATUS));
        Conferir("Compx status: dormindo", 0, (int)cxs[6]);
        var cxe = new byte[17]; cxe[0] = 0x08; cxe[1] = 0x04; cxe[6] = 76; cxe[7] = 0; cxe[8] = 0x0F; cxe[9] = 0x3C; cxe[16] = Compx.Soma(cxe);
        int cn, cmv; bool cc;
        Conferir("Compx energia 76% 3900 mV", "76|False|3900", Compx.Decodificar(cxe, out cn, out cc, out cmv) ? cn + "|" + cc + "|" + cmv : "sem leitura");
        cxe[7] = 1; cxe[16] = Compx.Soma(cxe);
        Conferir("Compx energia no cabo = carregando", "76|True", Compx.Decodificar(cxe, out cn, out cc, out cmv) ? cn + "|" + cc : "sem leitura");
        cxe[16] ^= 0xFF;
        Conferir("Compx checksum errado recusado", false, Compx.Decodificar(cxe, out cn, out cc, out cmv));

        // Logitech Unifying: registro 0x2B5, respostas reais do receptor C52B (K270 no espaco 2)
        Conferir("Unifying nome K270", "K270", Logi.NomeDoRegistro(new byte[] { 0x41, 0x04, 0x4B, 0x32, 0x37, 0x30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Conferir("Unifying tipo 1 = teclado", Tipo.Teclado, Logi.TipoDe10(new byte[] { 0x21, 0x08, 0x14, 0x40, 0x03, 0x04, 0x00, 0x01 }[7] & 0x0F));
        Conferir("Unifying tipo 2 = mouse", Tipo.Mouse, Logi.TipoDe10(0x02));

        // K270 por faixa: 20% (digitando) e depois 70% (parado) -> fica 20% por 12 h
        Func<int, Aparelho> k = nv => { var a = new Aparelho { Id = "k", PorFaixa = true }; a.Valores.Add(new Valor { Nivel = nv }); return a; };
        var mem = new Dictionary<string, Tuple<int, DateTime>>();
        var t = new DateTime(2026, 11, 24, 15, 0, 0);
        Coletor.Pior(mem, k(20), t);
        var kb = k(70); Coletor.Pior(mem, kb, t.AddHours(5));
        Conferir("faixa: 70 cinco horas depois de 20 fica 20", 20, kb.Valores[0].Nivel);
        var kc = k(20); Coletor.Pior(mem, kc, t.AddHours(10));
        var kd = k(70); Coletor.Pior(mem, kd, t.AddHours(21));
        Conferir("faixa: 20 de novo renova a janela", 20, kd.Valores[0].Nivel);
        var ke = k(70); Coletor.Pior(mem, ke, t.AddHours(22.5));
        Conferir("faixa: 12 h sem 20 (pilha nova) volta 70", 70, ke.Valores[0].Nivel);
        var kf = k(70); kf.PorFaixa = false; Coletor.Pior(new Dictionary<string, Tuple<int, DateTime>> { { "k", Tuple.Create(20, t) } }, kf, t.AddHours(1));
        Conferir("sem faixa: vale a leitura nova", 70, kf.Valores[0].Nivel);

        // padrao USB: valor bruto -> %
        Conferir("HID 0-255 valor 128", 50, Hid.Escala(128, 0, 255, false));
        Conferir("HID 0-100 valor 73", 73, Hid.Escala(73, 0, 100, false));
        Conferir("HID fora da faixa", -1, Hid.Escala(300, 0, 255, false));
        Conferir("HID 0x85/0x65 ja em %", 42, Hid.Escala(42, 0, 0, true));

        // Fast Pair: 2C FE | flags | campo 0x33 (3 bytes, tipo 3) E=90 D=35 estojo=60 carregando
        var fp = FastPair.Decodificar(new byte[] { 0x2C, 0xFE, 0x00, 0x33, 90, 35, 0x80 | 60 });
        Conferir("Fast Pair E/D/estojo", "5A-23-BC", fp == null ? "nada" : BitConverter.ToString(fp));

        // Main em STA (sem isso a area de transferencia falha: "Copiar diagnostico" da 1.1.0-beta/beta.2)
        var main = typeof(Programa).GetMethod("Main", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Conferir("Main marcado com [STAThread]", true, main != null && main.GetCustomAttributes(typeof(STAThreadAttribute), false).Length == 1);

        // versoes
        Conferir("1.1.0 > 1.0.0", true, Versao.Comparar("v1.1.0", "1.0.0") > 0);
        Conferir("1.1.0 > 1.1.0-beta", true, Versao.Comparar("1.1.0", "1.1.0-beta") > 0);
        Conferir("1.1.0-beta.2 > 1.1.0-beta", true, Versao.Comparar("v1.1.0-beta.2", "1.1.0-beta") > 0);
        Conferir("1.0.0 = v1.0.0", 0, Versao.Comparar("1.0.0", "v1.0.0"));
        Conferir("1.0.10 > 1.0.9", true, Versao.Comparar("1.0.10", "1.0.9") > 0);
        Conferir("beta reconhecida", true, Versao.Beta("1.1.0-beta"));

        log.AppendLine(falhas == 0 ? "TUDO OK" : falhas + " falha(s)");
        File.WriteAllText(arquivo, log.ToString(), System.Text.Encoding.UTF8);
        return falhas;
    }
}

static class Programa
{
    static void RegistrarErro(Exception ex)
    {
        try
        {
            var arq = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "erros.log");
            if (File.Exists(arq) && new FileInfo(arq).Length > 200 * 1024) File.Delete(arq);
            File.AppendAllText(arq, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " " + Atualizador.Atual + "\r\n" + ex + "\r\n\r\n", System.Text.Encoding.UTF8);
        }
        catch { }
    }

    static string Arg(string[] args, string nome)
    {
        int i = Array.FindIndex(args, a => a.Equals(nome, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        return i + 1 < args.Length ? args[i + 1] : "";
    }

    [STAThread]                              // area de transferencia e caixas de dialogo exigem STA
    static void Main(string[] args)
    {
        // --versao X: finge outra versao (para testar o aviso de atualizacao)
        var fingir = Arg(args, "--versao");
        if (!string.IsNullOrEmpty(fingir)) Atualizador.Atual = fingir;

        // --testar [arquivo]: roda os testes de decodificacao e sai (codigo de saida = falhas)
        var testar = Arg(args, "--testar");
        if (testar != null) { Environment.Exit(Testes.Rodar(testar.Length > 0 ? testar : Path.Combine(Path.GetTempPath(), "juicebar-testes.txt"))); return; }

        // --atualizar-agora: procura e instala a versao nova sem perguntar (o mesmo do menu)
        if (args.Any(a => a.Equals("--atualizar-agora", StringComparison.OrdinalIgnoreCase)))
        {
            string e1; var r = Atualizador.Procurar(out e1);
            Environment.Exit(r == null ? 2 : (Atualizador.Instalar(r) == null ? 0 : 1));
            return;
        }

        // --diagnostico [arquivo]: grava o diagnostico USB/HID e sai
        var diag = Arg(args, "--diagnostico");
        if (diag != null)
        {
            File.WriteAllText(diag.Length > 0 ? diag : Path.Combine(Path.GetTempPath(), "juicebar-diagnostico.txt"), Receptores.Ler().Diagnostico, System.Text.Encoding.UTF8);
            return;
        }

        bool novo;
        using (var m = new Mutex(true, "Juicebar", out novo))
        {
            if (!novo && args.Any(a => a.Equals("--esperar", StringComparison.OrdinalIgnoreCase)))
            {
                // depois de atualizar: espera a versao antiga fechar
                try { novo = m.WaitOne(15000); } catch (AbandonedMutexException) { novo = true; }
            }
            if (!novo) return;
            Atualizador.LimparVelho();
            Coletor.Demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
            N.SetProcessDPIAware();
            // erro inesperado na tela: grava em erros.log ao lado do .exe e segue, sem a caixa do .NET
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => RegistrarErro(e.Exception);
            Application.EnableVisualStyles();
            var w = new Widget();
            w.Show();
            // sem formulario principal: se o Explorer cair e destruir a janela da barra, o
            // programa continua e a janela volta sozinha (ver Encaixar). Fecha so pelo Sair.
            Application.Run(new ApplicationContext());
        }
    }
}
