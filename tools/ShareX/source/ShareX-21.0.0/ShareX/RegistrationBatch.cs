using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed record RegistrationStep(string Name,Func<Task<bool>> Ready,Func<IProgress<int>,CancellationToken,Task> Prepare);
    internal sealed record RegistrationProgress(int Percent,string Message);
    internal sealed record RegistrationBatchResult(int Prepared,int Skipped,IReadOnlyList<string> Errors,bool Cancelled);
    internal static class RegistrationBatch
    {
        private sealed class StepProgress : IProgress<int>
        {
            private readonly Action<int> report;
            internal StepProgress(Action<int> report)=>this.report=report;
            public void Report(int value)=>report(Math.Clamp(value,0,100));
        }
        internal static async Task<RegistrationBatchResult> RunAsync(IReadOnlyList<RegistrationStep> steps,IProgress<RegistrationProgress> progress,CancellationToken cancellation)
        {
            int prepared=0,skipped=0;var errors=new List<string>();bool cancelled=false;
            for(int index=0;index<steps.Count;index++)
            {
                if(cancellation.IsCancellationRequested){cancelled=true;break;}
                var step=steps[index];int position=index,lastPercent=0;
                void Report(int percent,string detail)
                {
                    percent=Math.Max(lastPercent,percent);lastPercent=percent;
                    progress?.Report(new RegistrationProgress((position*100+percent)/steps.Count,$"{position+1}/{steps.Count} {step.Name}：{detail}"));
                }
                try
                {
                    Report(0,"检查中");
                    if(await step.Ready()){skipped++;Report(100,"已就绪，跳过");continue;}
                    cancellation.ThrowIfCancellationRequested();
                    Report(0,"准备中");
                    await step.Prepare(new StepProgress(value=>Report(value,value<90?$"{value}%":"校验或注册中")),cancellation);
                    if(!await step.Ready())throw new InvalidOperationException("未通过完成状态检查，请重试此项。");
                    prepared++;Report(100,"完成");
                }
                catch(OperationCanceledException){cancelled=true;break;}
                catch(Win32Exception error) when(error.NativeErrorCode==1223){cancelled=true;break;}
                catch(Exception error){errors.Add(step.Name+"："+error.Message);Report(100,"失败，继续其他项");}
            }
            return new RegistrationBatchResult(prepared,skipped,errors,cancelled);
        }
    }
}
