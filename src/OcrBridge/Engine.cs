using RapidOcrNet;
namespace Xiaomang.OcrBridge;
public sealed class Engine : IDisposable
{
    private readonly RapidOcr engine = new();
    public Engine(string root)
    {
        using var options = RapidOcr.GetDefaultSessionOptions(2);
        options.EnableCpuMemArena = false; options.InterOpNumThreads = 1;
        try { engine.InitModels(Path.Combine(root,"ch_PP-OCRv5_det_mobile.onnx"),Path.Combine(root,"ch_ppocr_mobile_v2.0_cls_mobile.onnx"),Path.Combine(root,"ch_PP-OCRv5_rec_mobile.onnx"),Path.Combine(root,"keys.txt"),options); }
        catch { engine.Dispose(); throw; }
    }
    public string Recognize(string image) => engine.Detect(image, RapidOcrOptions.Default with { DoAngle=false, Padding=0, ImgResize=1500 }).StrRes;
    public void Dispose() => engine.Dispose();
}
