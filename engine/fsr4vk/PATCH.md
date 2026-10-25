# fsr4vk v0.4.3 with descriptor-heap diagnostics (test build)

This DLL is fsr4vk v0.4.3 with one small patch, built to test FSR 4.1.1 on AMD's Windows driver.

Symptom on an RX 6600 (driver 26.8.1, Vulkan 1.4.315):

    Upscaler: fsr4vk error: FSR4 Vulkan dispatch failed: attempt=1 stage=model/pipeline creation; descriptor binding exceeds queried layout size

What the patch changes:

1. The three descriptor heap bindings no longer use `VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT`. They stay at 128 descriptors. Suspected cause: AMD's driver reports a layout size that leaves out the variable-count binding's maximum, which makes fsr4vk's size check throw.
2. Before that size check runs, the provider reports one `descriptor_heaps` line through the host's message callback, so it shows up in `last_run.log` as `Upscaler: fsr4vk warning: descriptor_heaps ...`. It lists the descriptor sizes from the driver, and for each of the three sets the queried layout size, the binding offset, the stride fsr4vk computes, and the stride the driver implies (`(size - offset) / 128`). A difference between the two strides is marked `!!! STRIDE_MISMATCH`.

How to test:

1. Close the game.
2. In the install's `fsr4vk` folder, replace `amd_fidelityfx_upscaler_vk.dll` with the one in this zip. Keep the old one if you want to go back.
3. Run the game with FSR 4.1.1 selected.
4. Send back `last_run.log`, or at least the lines containing `fsr4vk`.

Do not run `tools/fetch_fsr4vk.py` without `--check` afterwards: it replaces any DLL whose hash differs from the release with the original. The launcher's download button does nothing while a DLL is present.

On an NVIDIA RTX 4070 the line reads (no mismatch):

    Upscaler: fsr4vk warning: descriptor_heaps robust=1 sampler=4 uniformBuffer(plain/robust)=8/8 storageBuffer=16/16 uniformTexelBuffer=16/16 storageTexelBuffer=16/16 sampledImage=4 storageImage=4 | set0 size=512 offset=0 stride=4 implied=4 | set1 size=2112 offset=64 stride=16 implied=16 | set2 size=2048 offset=0 stride=16 implied=16

The word "warning" is only how the port labels non-error provider messages.

## License and source

fsr4vk is GPLv3 (AMD FidelityFX data under MIT, zstd decoder under BSD); the licenses are in `LICENSES/`. This DLL is a modified version, so the GPLv3 applies to it and the corresponding source is:

- Upstream: https://github.com/dvj5411/fsr4vk, tag `v0.4.3` (commit 7c04e511195bf4420a060d64df84d625a37457e0)
- Plus the diff below, applied to that tag.

Built with clang 22.1.8 (MSYS2 CLANG64, MinGW target, UCRT) through upstream's `tools/build-vulkan-provider-windows.sh` with `FSR4_EMBED_ASSETS=assets/general`. The published release was built with MinGW gcc against msvcrt, so the file is not byte-identical in size or imports to the release even without the patch.

## Diff against v0.4.3

```diff
diff --git a/native/quality-context.hpp b/native/quality-context.hpp
index daea410..65d0c32 100644
--- a/native/quality-context.hpp
+++ b/native/quality-context.hpp
@@ -506,8 +506,9 @@ public:
         check(vkCreateSampler(device, &sampler_info, nullptr, &sampler),
               "vkCreateSampler");
 
-        const VkDescriptorBindingFlags variable_descriptor_count =
-            VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT;
+        // No VARIABLE_DESCRIPTOR_COUNT: descriptor buffers always use the full 128, and AMD's
+        // Windows driver may report a layout size that omits a variable-count binding's maximum.
+        const VkDescriptorBindingFlags heap_binding_flags = 0;
         const std::array<VkDescriptorType, 6> mutable_types{{
             VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
             VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
@@ -541,7 +542,7 @@ public:
         VkDescriptorSetLayoutBindingFlagsCreateInfo sampler_heap_flags{
             VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_BINDING_FLAGS_CREATE_INFO};
         sampler_heap_flags.bindingCount = 1;
-        sampler_heap_flags.pBindingFlags = &variable_descriptor_count;
+        sampler_heap_flags.pBindingFlags = &heap_binding_flags;
         create_set_layout(0, &sampler_heap_binding, 1, &sampler_heap_flags,
                           VK_DESCRIPTOR_SET_LAYOUT_CREATE_DESCRIPTOR_BUFFER_BIT_EXT);
 
@@ -552,7 +553,7 @@ public:
              VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
         }};
         const std::array<VkDescriptorBindingFlags, 2> image_heap_flag_values{{
-            0, variable_descriptor_count}};
+            0, heap_binding_flags}};
         const std::array<VkMutableDescriptorTypeListEXT, 2> image_heap_lists{{
             {0, nullptr}, mutable_list}};
         VkMutableDescriptorTypeCreateInfoEXT image_heap_mutable_info{
@@ -579,7 +580,7 @@ public:
             VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_BINDING_FLAGS_CREATE_INFO};
         buffer_heap_flags.pNext = &buffer_heap_mutable_info;
         buffer_heap_flags.bindingCount = 1;
-        buffer_heap_flags.pBindingFlags = &variable_descriptor_count;
+        buffer_heap_flags.pBindingFlags = &heap_binding_flags;
         create_set_layout(2, &buffer_heap_binding, 1, &buffer_heap_flags,
                           VK_DESCRIPTOR_SET_LAYOUT_CREATE_DESCRIPTOR_BUFFER_BIT_EXT);
 
@@ -664,6 +665,31 @@ public:
             get_binding_offset(device, set_layouts[index], descriptor_bindings[index],
                                &descriptor_binding_offsets[index]);
         }
+        if(build_message) {
+            // One line, logged before DescriptorLayout can throw, so a failing driver still reports it.
+            const auto& p=descriptor_properties;
+            const auto robust_text=robust_buffer_access ? std::to_string(int(*robust_buffer_access)) : std::string("unknown");
+            const auto pair=[](VkDeviceSize plain,VkDeviceSize robust){return std::to_string(plain)+"/"+std::to_string(robust);};
+            std::string line="descriptor_heaps robust="+robust_text+
+                " sampler="+std::to_string(p.samplerDescriptorSize)+
+                " uniformBuffer(plain/robust)="+pair(p.uniformBufferDescriptorSize,p.robustUniformBufferDescriptorSize)+
+                " storageBuffer="+pair(p.storageBufferDescriptorSize,p.robustStorageBufferDescriptorSize)+
+                " uniformTexelBuffer="+pair(p.uniformTexelBufferDescriptorSize,p.robustUniformTexelBufferDescriptorSize)+
+                " storageTexelBuffer="+pair(p.storageTexelBufferDescriptorSize,p.robustStorageTexelBufferDescriptorSize)+
+                " sampledImage="+std::to_string(p.sampledImageDescriptorSize)+
+                " storageImage="+std::to_string(p.storageImageDescriptorSize);
+            const auto mutable_stride=fsr4vk::DescriptorLayout::mutable_size(p,robust_buffer_access.value_or(false));
+            for (std::size_t index=0;index<descriptor_layout_sizes.size();++index) {
+                const auto computed=index==0 ? p.samplerDescriptorSize : mutable_stride;
+                const auto size=descriptor_layout_sizes[index],offset=descriptor_binding_offsets[index];
+                const auto implied=size>=offset ? (size-offset)/kDescriptorCount : VkDeviceSize(0);
+                line+=" | set"+std::to_string(index)+" size="+std::to_string(size)+" offset="+std::to_string(offset)+
+                      " stride="+std::to_string(computed)+" implied="+std::to_string(implied);
+                if(implied!=computed)
+                    line+=std::string(" !!! STRIDE_MISMATCH")+(implied<computed ? " (implied<computed: layout check will fail)" : " (implied>computed)");
+            }
+            build_message(line);
+        }
         // Properties determine descriptor strides; queried set sizes only bound
         // the allocation. Drivers are allowed to pad the overall layouts.
         descriptor_layout=fsr4vk::DescriptorLayout(descriptor_properties,descriptor_layout_sizes,
diff --git a/provider/ffx_vk_provider.cpp b/provider/ffx_vk_provider.cpp
index f265dcc..a84f677 100644
--- a/provider/ffx_vk_provider.cpp
+++ b/provider/ffx_vk_provider.cpp
@@ -806,8 +806,11 @@ static ffxReturnCode_t dispatchImpl(
                 provider->apiVersion,
                 (provider->flags & FFX_UPSCALE_ENABLE_AUTO_EXPOSURE)==0,provider->version==kVersion411Id,
                 provider->robust_buffer_access,provider->native_mixed_dot_enabled,
-                provider->diagnostic_path.empty() ? std::function<void(const std::string&)>{} :
-                    std::function<void(const std::string&)>{[path=provider->diagnostic_path,id=provider->diagnostic_id](const auto& text){write_diagnostic(path,id,text);}},
+                // The descriptor_heaps line also goes to the host's message callback (the port's log);
+                // it must not depend on FSR4_VK_LOG being set.
+                std::function<void(const std::string&)>{[provider,path=provider->diagnostic_path,id=provider->diagnostic_id](const std::string& text){
+                    if(text.rfind("descriptor_heaps ",0)==0) provider_message(*provider,FFX_API_MESSAGE_TYPE_WARNING,text);
+                    else write_diagnostic(path,id,text);}},
                 provider->queue_family_index);
             core=created.get();
             provider->cores[selected_preset]=std::move(created);
```
