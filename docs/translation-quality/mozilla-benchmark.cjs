// Research-only harness. Node is not a dependency of the Windows product.
// Run: node mozilla-benchmark.cjs CACHE_DIR CORPUS_JSON OUTPUT_JSON [shortlist|full]
const fs = require('fs'), vm = require('vm'), path = require('path'), crypto = require('crypto');
const [cache, corpusFile, outputFile, mode='shortlist'] = process.argv.slice(2);
if (!['shortlist','full'].includes(mode)) throw Error('Expected shortlist or full');
const corpus=JSON.parse(fs.readFileSync(corpusFile,'utf8'));
const lock=JSON.parse(fs.readFileSync(path.join(__dirname,'mozilla-artifact-lock.json'),'utf8'));
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
const checked=(file,hash)=>{const b=fs.readFileSync(path.join(cache,file));if(sha(b)!==hash)throw Error(`Hash mismatch: ${file}`);return b;};
const glue=checked('bergamot-translator.js',lock.glue.sha256);
const wasm=checked('bergamot-translator.wasm',lock.runtime.decompressedHash);
vm.runInThisContext(glue.toString());
const start=performance.now(), cpuStart=process.cpuUsage(); let M;
const result={hardware:require('os').cpus()[0]?.model,mode,startedUtc:new Date().toISOString(),platform:process.platform,arch:process.arch,node:process.version,engine:'Mozilla official v0.6.0 WASM; Node fallback GEMM (not Firefox mozIntGemm)',corpusSha256:sha(fs.readFileSync(corpusFile)),directions:[],outputs:[]};
const save=()=>fs.writeFileSync(outputFile,JSON.stringify(result,null,2)+'\n');
M=loadBergamot({wasmBinary:wasm,print:()=>{},printErr:s=>process.stderr.write(s+'\n'),onRuntimeInitialized(){try{
 result.runtimeInitMs=performance.now()-start;
 for(const [source,target] of [['en','ru'],['ru','en']]){
  const t0=performance.now(),cpu0=process.cpuUsage(),pair=source+target,owned=[];
  const allocate=(type,alignment)=>{const rec=lock.models.find(x=>x.sourceLanguage===source&&x.targetLanguage===target&&x.fileType===type);const bytes=checked(pair+'-'+type,rec.decompressedHash),mem=new M.AlignedMemory(bytes.length,alignment);mem.getByteArrayView().set(bytes);owned.push(mem);return mem;};
  const model=allocate('model',256),lex=mode==='shortlist'?allocate('lex',64):null,vocab=allocate('vocab',64),vocabs=new M.AlignedMemoryList();vocabs.push_back(vocab);
  const config={'beam-size':1,normalize:'1.0','word-penalty':0,'max-length-break':128,'mini-batch-words':1024,workspace:128,'max-length-factor':'2.0','skip-cost':true,'cpu-threads':0,quiet:true,'quiet-translation':true,'gemm-precision':'int8shiftAlphaAll',alignment:'soft'};
  const configText=Object.entries(config).map(([k,v])=>`${k}: ${v}`).join('\n')+'\n';
  const tm=new M.TranslationModel(source,target,configText,model,lex,vocabs,null),service=new M.BlockingService({cacheSize:0});
  const detail={source,target,loadMs:performance.now()-t0,loadCpuMicroseconds:process.cpuUsage(cpu0),config,wasmHeapBytes:M.HEAPU8.buffer.byteLength,afterLoad:process.memoryUsage()};result.directions.push(detail);save();
  for(const item of corpus.filter(x=>x.source===source&&x.target===target)){
   const messages=new M.VectorString(),options=new M.VectorResponseOptions();messages.push_back(item.text);options.push_back({qualityScores:false,alignment:true,html:false});
   const ts=performance.now(),cs=process.cpuUsage();const responses=service.translate(tm,messages,options);const cpu=process.cpuUsage(cs);const response=responses.get(0),translated=response.getTranslatedText();
   result.outputs.push({id:item.id,source,target,input:item.text,translated,elapsedMs:performance.now()-ts,cpuMicroseconds:cpu,rssBytes:process.memoryUsage().rss,wasmHeapBytes:M.HEAPU8.buffer.byteLength});
   response.delete();responses.delete();messages.delete();options.delete();save();
  }
  detail.endMemory=process.memoryUsage();tm.delete();service.delete();vocabs.delete();for(const mem of owned)mem.delete();
 }
 result.totalMs=performance.now()-start;result.totalCpuMicroseconds=process.cpuUsage(cpuStart);result.resourceUsage=process.resourceUsage();save();console.log(outputFile,result.outputs.length,result.totalMs);
}catch(error){result.error=String(error.stack||error);save();console.error(error);process.exitCode=1;}}});
