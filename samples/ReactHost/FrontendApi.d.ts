// @namespace ReactHost.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
}
