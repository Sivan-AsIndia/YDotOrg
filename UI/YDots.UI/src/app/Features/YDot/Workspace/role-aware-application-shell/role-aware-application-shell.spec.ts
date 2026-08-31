import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RoleAwareApplicationShellComponent } from './role-aware-application-shell';

describe('RoleAwareApplicationShellComponent', () => {
  let component: RoleAwareApplicationShellComponent;
  let fixture: ComponentFixture<RoleAwareApplicationShellComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RoleAwareApplicationShellComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RoleAwareApplicationShellComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
