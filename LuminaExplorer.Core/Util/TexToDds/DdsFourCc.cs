namespace LuminaExplorer.Core.Util.TexToDds; 

public enum DdsFourCc {
    Dxt1 = 0x31545844,
    Dxt3 = 0x33545844,
    Dxt5 = 0x35545844,
    Ati2 = 0x32495441,
    
    Bc1 = Dxt1,
    Bc2 = Dxt3,
    Bc3 = Dxt5,
    Bc5 = Ati2,
    
    Dx10 = 0x30315844,
}
