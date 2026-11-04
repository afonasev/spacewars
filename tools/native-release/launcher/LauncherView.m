#import <Cocoa/Cocoa.h>
#import <dispatch/dispatch.h>
#import <QuartzCore/QuartzCore.h>

@interface Launcher : NSObject <NSApplicationDelegate, NSWindowDelegate>
@property NSWindow *window;
@property NSTextField *version, *message, *heading;
@property NSButton *play, *secondary;
@property NSProgressIndicator *progress;
@property BOOL busy;
@end
@implementation Launcher
- (NSTextField *)label:(NSString *)s size:(CGFloat)size y:(CGFloat)y {
 NSTextField *v=[NSTextField labelWithString:s]; v.frame=NSMakeRect(26,y,568,40);
 v.font=[NSFont systemFontOfSize:size weight:NSFontWeightMedium];v.textColor=NSColor.labelColor;
 [self.window.contentView addSubview:v];return v;
}
- (void)applicationDidFinishLaunching:(NSNotification *)n {
 self.window=[[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,620,350) styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskMiniaturizable backing:NSBackingStoreBuffered defer:NO];
 self.window.title=@"Spacewars";self.window.delegate=self;self.window.releasedWhenClosed=NO;
 self.window.appearance=[NSAppearance appearanceNamed:NSAppearanceNameDarkAqua];
 self.window.backgroundColor=[NSColor colorWithSRGBRed:0.055 green:0.09 blue:0.14 alpha:1];
 // Fixed cover uses the same packaged planet logo as the game icon.
 NSImage *logo=[[NSImage alloc] initWithContentsOfFile:[[NSBundle mainBundle] pathForResource:@"LauncherLogo" ofType:@"png"]];
 NSView *cover=[[NSView alloc] initWithFrame:NSMakeRect(26,264,568,64)];cover.wantsLayer=YES;
 cover.layer.contents=(__bridge id)[logo CGImageForProposedRect:NULL context:nil hints:nil];
 cover.layer.contentsGravity=@"resizeAspectFill";cover.layer.masksToBounds=YES;cover.layer.cornerRadius=10;
 [self.window.contentView addSubview:cover];
 NSView *caption=[[NSView alloc] initWithFrame:NSMakeRect(0,0,280,64)];caption.wantsLayer=YES;
 caption.layer.backgroundColor=[NSColor colorWithSRGBRed:.055 green:.09 blue:.14 alpha:.9].CGColor;[cover addSubview:caption];
 NSTextField *title=[NSTextField labelWithString:@"SPACEWARS"];title.frame=NSMakeRect(18,17,248,32);title.font=[NSFont systemFontOfSize:20 weight:NSFontWeightBold];title.textColor=NSColor.whiteColor;[caption addSubview:title];
 self.heading=[self label:@"" size:25 y:202];self.heading.font=[NSFont systemFontOfSize:25 weight:NSFontWeightBold];
 self.version=[self label:@"" size:12 y:82];self.version.maximumNumberOfLines=2;self.version.textColor=NSColor.secondaryLabelColor;
 self.message=[self label:@"" size:14 y:137];self.message.maximumNumberOfLines=3;self.message.textColor=NSColor.secondaryLabelColor;self.message.frame=NSMakeRect(26,137,568,62);
 self.progress=[[NSProgressIndicator alloc] initWithFrame:NSMakeRect(26,73,568,6)];self.progress.style=NSProgressIndicatorStyleBar;self.progress.minValue=0;self.progress.maxValue=100;self.progress.hidden=YES;[self.window.contentView addSubview:self.progress];
 self.play=[NSButton buttonWithTitle:@"Играть сейчас" target:self action:@selector(playClicked:)];self.play.frame=NSMakeRect(250,20,174,44);self.play.bezelStyle=NSBezelStyleRounded;self.play.font=[NSFont systemFontOfSize:14 weight:NSFontWeightSemibold];self.play.keyEquivalent=@"\r";self.play.bordered=NO;self.play.wantsLayer=YES;self.play.layer.cornerRadius=8;self.play.layer.backgroundColor=[NSColor colorWithSRGBRed:.3 green:.84 blue:.8 alpha:1].CGColor;self.play.contentTintColor=[NSColor colorWithSRGBRed:.035 green:.14 blue:.15 alpha:1];[self.window.contentView addSubview:self.play];
 self.secondary=[NSButton buttonWithTitle:@"" target:self action:@selector(secondaryClicked:)];self.secondary.frame=NSMakeRect(436,20,158,44);self.secondary.bezelStyle=NSBezelStyleRounded;self.secondary.font=[NSFont systemFontOfSize:14 weight:NSFontWeightSemibold];self.secondary.bordered=NO;self.secondary.wantsLayer=YES;self.secondary.layer.cornerRadius=8;self.secondary.layer.backgroundColor=[NSColor colorWithSRGBRed:.14 green:.21 blue:.29 alpha:1].CGColor;self.secondary.hidden=YES;[self.window.contentView addSubview:self.secondary];
 [self.window center];[self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
 dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
  char *line=NULL;size_t cap=0;ssize_t length;
  while((length=getline(&line,&cap,stdin))>0){
   if(length>16384)break;
   NSData *data=[NSData dataWithBytes:line length:length];id json=[NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
   if([json isKindOfClass:NSDictionary.class]){NSDictionary *state=json;dispatch_async(dispatch_get_main_queue(), ^{[self render:state];});}
  }
  free(line);dispatch_async(dispatch_get_main_queue(), ^{[NSApp terminate:nil];});
 });
}
- (void)render:(NSDictionary *)s {
 self.heading.stringValue=s[@"title"]?:@"Игра готова к запуску";
 self.version.stringValue=s[@"version"]?:@"";self.message.stringValue=s[@"message"]?:@"";
 self.busy=[s[@"busy"] boolValue];self.play.title=s[@"primary"]?:@"Играть сейчас";self.play.enabled=!self.busy;
 NSString *secondary=s[@"secondary"]?:@"";self.secondary.title=secondary;self.secondary.hidden=secondary.length==0;self.secondary.enabled=!self.busy;
 BOOL loading=[self.message.stringValue hasPrefix:@"Загрузка"]||[self.message.stringValue isEqual:@"Установка…"];
 NSInteger progress=[s[@"progress"] integerValue];self.progress.hidden=!loading;self.progress.indeterminate=progress<0;
 if(loading&&progress<0)[self.progress startAnimation:nil];else{[self.progress stopAnimation:nil];self.progress.doubleValue=progress;}
 if(self.secondary.hidden)self.play.frame=NSMakeRect(420,20,174,44);else self.play.frame=NSMakeRect(250,20,174,44);
 BOOL updateAccent=!self.secondary.hidden&&![secondary isEqual:@"Отменить"];
 NSColor *accent=[NSColor colorWithSRGBRed:.3 green:.84 blue:.8 alpha:1],*neutral=[NSColor colorWithSRGBRed:.14 green:.21 blue:.29 alpha:1],*ink=[NSColor colorWithSRGBRed:.035 green:.14 blue:.15 alpha:1];
 self.play.layer.backgroundColor=(updateAccent?neutral:accent).CGColor;self.play.contentTintColor=updateAccent?NSColor.whiteColor:ink;
 self.secondary.layer.backgroundColor=(updateAccent?accent:neutral).CGColor;self.secondary.contentTintColor=updateAccent?ink:NSColor.whiteColor;
}
- (void)playClicked:(id)sender {if(!self.busy){puts("play");fflush(stdout);}}
- (void)secondaryClicked:(id)sender {if(!self.busy){puts("secondary");fflush(stdout);}}
- (BOOL)applicationSupportsSecureRestorableState:(NSApplication *)app{return YES;}
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)app{return YES;}
@end
int main(){@autoreleasepool{[NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];Launcher *delegate=[Launcher new];NSApp.delegate=delegate;[NSApp run];}return 0;}
